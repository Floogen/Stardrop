using System;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Newtonsoft.Json;
using Stardrop.Models.Nexus.Web;

namespace Stardrop.Utilities.External
{
    public class NexusGraphQLClient
    {
        private GraphQLHttpClient _client;

        public NexusGraphQLClient(HttpClient client)
        {
            _client = new GraphQLHttpClient(
                "https://api.nexusmods.com/v2/graphql",
                new NewtonsoftJsonSerializer(),
                client
            );
        }

        public async Task<CollectionResult?> GetCollection(string slug) // slug is the id at the end of the url
        {
            Program.helper.Log("getting collection download link");
            GraphQLRequest query = new()
            {
                Query = @"
                    query Collection($slug: String!, $domain: String!) {
                        collection(slug: $slug, viewAdultContent: false, domainName: $domain) {
                            gameId
                            id
                            name
                            summary
                            latestPublishedRevision {
                                downloadLink
                            }
                        }
                    }
                ",
                Variables = new
                {
                    slug = slug,
                    domain = "stardewvalley"
                },
                OperationName = "Collection"
            };

            var res = await _client.SendQueryAsync<CollectionResult>(query);

            if (res.Errors != null && res.Errors.Length > 0)
            {
                foreach (var error in res.Errors)
                {
                    Program.helper.Log($"Got error while getting collection download link: {error.Message}");
                }

                return null;
            }

            return res.Data;

        }
    }
}