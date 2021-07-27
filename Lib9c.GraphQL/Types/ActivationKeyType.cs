using GraphQL.Types;
using Nekoyume.Model;

namespace Lib9c.GraphQL.Types
{
    public class ActivationKeyType : ObjectGraphType<ActivationKey>
    {
        public ActivationKeyType()
        {
            Field(x => x.PendingAddress);
            Field(x => x.PrivateKey);

            Name = "ActivationKey";
        }
    }
}
