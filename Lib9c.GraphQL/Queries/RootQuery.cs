using GraphQL.Types;
using Libplanet.Action;

namespace Lib9c.GraphQL.Queries
{
    public class RootQuery<T> : ObjectGraphType
         where T: IAction, new()
    {
        public RootQuery()
        {
            Field<ActivatedAccountsQuery<T>>(
                name: "activatedAccountsQuery",
                resolve: context => new { }
            );
        }
    }
}
