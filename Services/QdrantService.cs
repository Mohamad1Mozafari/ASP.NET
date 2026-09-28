using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace RoshedTehran.Services
{
    public class QdrantService
    {
        private readonly QdrantClient _client;
        private const string CollectionName = "Entities";

        public QdrantService(IConfiguration config)
        {
            var host = config["Qdrant:Host"] ?? "localhost";
            var port = config.GetValue<int?>("Qdrant:Port") ?? 6334;
            _client = new QdrantClient(host, port);
        }

        // 768 for paraphrase-multilingual-mpnet-base-v2
        public async Task InitializeAsync(int vectorSize = 768)
        {
            var exists = await _client.CollectionExistsAsync(CollectionName);
            if (!exists)
            {
                await _client.CreateCollectionAsync(CollectionName, new VectorParams
                {
                    Size = (uint)vectorSize,
                    Distance = Distance.Cosine
                });
            }
        }

        // Payload only ever stores a pointer back to Postgres (id) plus a
        // couple of fields useful for a quick preview; everything else
        // (phones, emails, social links, full text) lives in Postgres.
        public async Task SaveEntityAsync(Guid postgresId, string titleTag, float[] vector, string? contactInfoChunk = null)
        {
            var payload = new Dictionary<string, string>
            {
                ["id"] = postgresId.ToString(),
                ["TitleTag"] = titleTag ?? "Unknown"
            };
            if (!string.IsNullOrWhiteSpace(contactInfoChunk))
                payload["ContactInfoChunk"] = contactInfoChunk;

            var point = new PointStruct
            {
                Id = postgresId,
                Vectors = vector
            };
            foreach (var kv in payload)
                point.Payload[kv.Key] = kv.Value;

            await _client.UpsertAsync(collectionName: CollectionName, points: new List<PointStruct> { point });
        }

        public async Task<IEnumerable<ScoredPoint>> SearchAsync(float[] queryVector, int limit = 10)
        {
            return await _client.SearchAsync(CollectionName, queryVector, limit: (uint)limit);
        }

        public async Task DeleteEntityAsync(Guid id)
        {
            await _client.DeleteAsync(collectionName: CollectionName, filter: MatchKeyword("id", id.ToString()));
        }
    }
}
