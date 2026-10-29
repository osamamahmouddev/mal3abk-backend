using Xunit;

// Every integration test boots the API against one shared PostgreSQL/Redis instance. Running test
// classes in parallel makes them race on shared state — e.g. Hangfire creating its schema from several
// hosts at once on a fresh database — so they run one class at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
