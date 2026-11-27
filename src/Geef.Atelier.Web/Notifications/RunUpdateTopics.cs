namespace Geef.Atelier.Web.Notifications;

/// <summary>
/// Names of the topics of <see cref="IRunUpdateFeed"/>. Clients never choose them; the server derives them from the signed-in user.
/// </summary>
public static class RunUpdateTopics
{
    /// <summary>Topic that receives list updates for every user's runs (administrators only).</summary>
    public const string AdminRuns = "runs-admin";

    /// <summary>Topic for updates of one run.</summary>
    public static string Run(Guid runId) => $"run-{runId}";

    /// <summary>Topic for list updates of the runs owned by <paramref name="username"/>.</summary>
    public static string UserRuns(string username) => $"runs-user-{username}";

    /// <summary>Topic that receives list updates for every user's orders (administrators only).</summary>
    public const string AdminOrders = "orders-admin";

    /// <summary>Topic for updates of one order.</summary>
    public static string Order(Guid orderId) => $"order-{orderId}";

    /// <summary>Topic for list updates of the orders owned by <paramref name="username"/>.</summary>
    public static string UserOrders(string username) => $"orders-user-{username}";
}
