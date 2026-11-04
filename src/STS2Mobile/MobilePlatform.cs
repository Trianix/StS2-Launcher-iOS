using System;

namespace STS2Mobile;

internal static class MobilePlatform
{
    internal static bool IsMobile => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
}
