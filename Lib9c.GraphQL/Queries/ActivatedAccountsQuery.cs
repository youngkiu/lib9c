using GraphQL.Types;
using Libplanet.Action;
using Libplanet.Explorer.Interfaces;

namespace Lib9c.GraphQL.Queries
{
    public class ActivatedAccountsQuery<T> : ObjectGraphType
        where T : IAction, new()
    {
        private IBlockChainContext<T> _chainContext;

        public ActivatedAccountsQuery(IBlockChainContext<T> context)
        {
            _chainContext = context;

            Field<NonNullGraphType<StringGraphType>>(
                "test", 
                resolve: _ =>
                {
                    return _chainContext.BlockChain.Tip.Hash.ToString();
                }
            );
        }
    }
}
