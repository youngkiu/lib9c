using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Bencodex.Types;
using Libplanet;
using Libplanet.Action;
using Libplanet.Assets;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.RocksDBStore;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Nekoyume.Action;
using Nekoyume.BlockChain;
using Nekoyume.Model.State;
using Serilog;
using Serilog.Events;
using NCAction = Libplanet.Action.PolymorphicAction<Nekoyume.Action.ActionBase>;

namespace Lib9c.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Too few arguments.");
                Environment.Exit(1);
                return;
            }

            string storePath = args[0];
            int limit = int.Parse(args[1]);
            Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console().CreateLogger();
            Libplanet.Crypto.CryptoConfig.CryptoBackend = new Secp256K1CryptoBackend<SHA256>();
            var policySource = new BlockPolicySource(Log.Logger, LogEventLevel.Verbose);
            IBlockPolicy<NCAction> policy =
                policySource.GetPolicy(5000000, 10000);
            IStagePolicy<NCAction> stagePolicy = new VolatileStagePolicy<NCAction>();
            var store = new MonoRocksDBStore(storePath);
            if (!(store.GetCanonicalChainId() is Guid chainId))
            {
                Console.Error.WriteLine("There is no canonical chain: {0}", storePath);
                Environment.Exit(1);
                return;
            }

            if (!(store.IndexBlockHash(chainId, 0) is { } gHash))
            {
                Console.Error.WriteLine("There is no genesis block: {0}", storePath);
                Environment.Exit(1);
                return;
            }

            Block<NCAction> genesis = store.GetBlock<NCAction>(gHash);
            var mainChain = MainChain(policy, stagePolicy, genesis);
            if (mainChain.GetState(AuthorizedMinersState.Address) is Dictionary ams &&
                policy is BlockPolicy bp)
            {
                bp.AuthorizedMinersState = new AuthorizedMinersState(ams);
            }
            IKeyValueStore stateRootKeyValueStore = new RocksDBKeyValueStore(Path.Combine(storePath, "state_hashes")),
                stateKeyValueStore = new RocksDBKeyValueStore(Path.Combine(storePath, "states"));
            IStateStore stateStore = new TrieStateStore(stateKeyValueStore, stateRootKeyValueStore);
            var chain = new BlockChain<NCAction>(policy, stagePolicy, store, stateStore, genesis);
            Log.Debug($"{chain.Count}, {mainChain.Count}");
            var index = 2129034;
            while (chain.Count < index)
            {
                var block = mainChain[chain.Count];
                chain.Append(block);
            }
        }

        // Copied from BlockChain<T>.SetStates().
        private static void SetStates(
            Guid chainId,
            IStateStore stateStore,
            Block<NCAction> block,
            IReadOnlyList<ActionEvaluation> actionEvaluations,
            bool buildStateReferences
        )
        {
            IImmutableSet<Address> stateUpdatedAddresses = actionEvaluations
                .SelectMany(a => a.OutputStates.StateUpdatedAddresses)
                .ToImmutableHashSet();
            IImmutableSet<(Address, Currency)> updatedFungibleAssets = actionEvaluations
                .SelectMany(a => a.OutputStates.UpdatedFungibleAssets
                    .SelectMany(kv => kv.Value.Select(c => (kv.Key, c))))
                .ToImmutableHashSet();

            if (!stateStore.ContainsBlockStates(block.Hash))
            {
                var totalDelta = GetTotalDelta(actionEvaluations, ToStateKey, ToFungibleAssetKey);
                stateStore.SetStates(block, totalDelta);
            }
        }

        // Copied from ActionEvaluationsExtensions.GetTotalDelta().
        private static ImmutableDictionary<string, IValue> GetTotalDelta(
            IReadOnlyList<ActionEvaluation> actionEvaluations,
            Func<Address, string> toStateKey,
            Func<(Address, Currency), string> toFungibleAssetKey)
        {
            IImmutableSet<Address> stateUpdatedAddresses = actionEvaluations
                .SelectMany(a => a.OutputStates.StateUpdatedAddresses)
                .ToImmutableHashSet();
            IImmutableSet<(Address, Currency)> updatedFungibleAssets = actionEvaluations
                .SelectMany(a => a.OutputStates.UpdatedFungibleAssets
                    .SelectMany(kv => kv.Value.Select(c => (kv.Key, c))))
                .ToImmutableHashSet();

            IAccountStateDelta lastStates = actionEvaluations.Count > 0
                ? actionEvaluations[actionEvaluations.Count - 1].OutputStates
                : null;
            ImmutableDictionary<string, IValue> totalDelta =
                stateUpdatedAddresses.ToImmutableDictionary(
                    toStateKey,
                    a => lastStates?.GetState(a)
                ).SetItems(
                    updatedFungibleAssets.Select(pair =>
                        new KeyValuePair<string, IValue>(
                            toFungibleAssetKey(pair),
                            new Bencodex.Types.Integer(
                                lastStates?.GetBalance(pair.Item1, pair.Item2).RawValue ?? 0
                            )
                        )
                    )
                );

            return totalDelta;
        }

        // Copied from KeyConverters.ToStateKey().
        private static string ToStateKey(Address address) => address.ToHex().ToLowerInvariant();

        // Copied from KeyConverters.ToFungibleAssetKey().
        private static string ToFungibleAssetKey(Address address, Currency currency) =>
            "_" + address.ToHex().ToLowerInvariant() +
            "_" + ByteUtil.Hex(currency.Hash.ByteArray).ToLowerInvariant();

        // Copied from KeyConverters.ToFungibleAssetKey().
        private static string ToFungibleAssetKey((Address, Currency) pair) =>
            ToFungibleAssetKey(pair.Item1, pair.Item2);

        private static BlockChain<NCAction> MainChain(IBlockPolicy<NCAction> policy, IStagePolicy<NCAction> stagePolicy,
            Block<NCAction> genesis)
        {
            string mainPath = @"C:\Users\qoora\AppData\Local\planetarium\9c-main-partition";
            var store = new RocksDBStore(mainPath);
            IKeyValueStore stateRootKeyValueStore = new RocksDBKeyValueStore(Path.Combine(mainPath, "state_hashes")),
                stateKeyValueStore = new RocksDBKeyValueStore(Path.Combine(mainPath, "states"));
            IStateStore stateStore = new TrieStateStore(stateKeyValueStore, stateRootKeyValueStore);
            return new BlockChain<NCAction>(policy, stagePolicy, store, stateStore, genesis);
        }
    }
}
