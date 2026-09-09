namespace Strunika.Mobile.Platforms.iOS;

/// <summary>
/// Somewhere to keep notification tokens for the life of the app.
/// <para>
/// Every <c>ObserveXxx</c> hands back a token, and dropping it is a crash, not
/// a leak: the notification centre does not retain an observer, so the token
/// is the only thing holding it. Once the token is collected its finaliser
/// releases the native observer <b>without</b> unregistering it — the
/// framework only unregisters from an explicit <c>Dispose</c> — and the centre
/// is left pointing at freed memory. The next route change or interruption
/// then lands on a dead object; on a tester's phone that read as
/// EXC_BAD_ACCESS in <c>objc_release</c> while the main queue drained
/// (build 37, 2026-09-09), minutes after a first garbage collection had
/// quietly taken every observer the app had registered at start-up.
/// </para>
/// <para>So: never discard a token. Anything meant to live as long as the app
/// goes through <see cref="Keep"/>.</para>
/// </summary>
internal static class Observers
{
    private static readonly List<object> Held = new();

    public static T Keep<T>(T token) where T : class
    {
        lock (Held) Held.Add(token);
        return token;
    }
}
