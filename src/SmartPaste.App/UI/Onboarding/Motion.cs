using System.Windows;
using System.Windows.Media.Animation;

namespace SmartPaste.App.UI.Onboarding;

// Shared motion rules for the welcome tour: short, decelerating entrances, faster exits, and nothing that moves
// when Windows' "Animation effects" setting is off.
internal static class Motion
{
    public static readonly Duration Exit = new(TimeSpan.FromMilliseconds(90));
    public static readonly Duration Enter = new(TimeSpan.FromMilliseconds(220));

    public static readonly IEasingFunction EaseOut = Freeze(new QuinticEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseIn = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });

    // Windows Settings > Accessibility > Visual effects > Animation effects. Off means: show end states, don't move.
    public static bool IsReduced => !SystemParameters.ClientAreaAnimation;

    private static IEasingFunction Freeze(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }
}
