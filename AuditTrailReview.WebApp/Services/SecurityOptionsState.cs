using System.Threading;
using AuditTrailReview.Model;
using Microsoft.Extensions.Primitives;

namespace AuditTrailReview.WebApp.Services
{
    /// <summary>
    /// Holds the password/lockout/sign-in policy currently in effect, plus the reload change
    /// token that lets ASP.NET Core's Options pattern (<c>IOptionsMonitor&lt;IdentityOptions&gt;</c>)
    /// pick up a change without an app restart -- see the configure-options/change-token-source/
    /// live-accessor classes in SecurityIdentityOptionsSetup.cs for how this is wired into
    /// <c>IdentityOptions</c>. The admin UI (AdminPanel/Settings/Index.cshtml.cs) calls
    /// <see cref="Reload"/> right after it saves a new <see cref="SecuritySettings"/> row, so the
    /// very next request picks up the change -- no restart needed, replacing the previous
    /// "IdentityOptions is bound once at startup" behavior.
    ///
    /// The change-token swap below is the same technique .NET's own configuration reload token
    /// uses: holding a <see cref="CancellationTokenSource"/> and replacing it (after cancelling
    /// the old one) on every reload, so anything that captured the previous token via
    /// <see cref="GetReloadToken"/> observes it fire exactly once.
    /// </summary>
    public sealed class SecurityOptionsState
    {
        private readonly object _gate = new();
        private SecuritySettings _current;
        private CancellationTokenSource _cts = new();

        public SecurityOptionsState(SecuritySettings initial)
        {
            _current = initial;
        }

        public SecuritySettings Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        public IChangeToken GetReloadToken()
        {
            lock (_gate)
            {
                return new CancellationChangeToken(_cts.Token);
            }
        }

        public void Reload(SecuritySettings updated)
        {
            CancellationTokenSource previous;
            lock (_gate)
            {
                _current = updated;
                previous = _cts;
                _cts = new CancellationTokenSource();
            }

            previous.Cancel();
            previous.Dispose();
        }
    }
}
