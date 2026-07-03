using Xunit;

// Integration tests share one PostgreSQL container and reset the DB before each
// test, so they must run serially. Disable parallelization assembly-wide.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
