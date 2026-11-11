using System;

namespace STS2Mobile.Patches;

internal static class TransitionAttempt
{
    internal static bool Run(Action action, Action rollback, Action<Exception> failed)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception original)
        {
            try { rollback(); }
            catch (Exception recovery) { original = new AggregateException(original, recovery); }
            failed(original);
            return false;
        }
    }
}
