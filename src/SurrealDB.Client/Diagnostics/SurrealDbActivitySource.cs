namespace SurrealDB.Client.Diagnostics;

using System.Diagnostics;

public static class SurrealDbActivitySource
{
    public static readonly ActivitySource Source = new("SurrealDB.Client", "1.0.0");

    internal static Activity? StartOperation(string operationName)
        => Source.StartActivity(operationName, ActivityKind.Client);
}
