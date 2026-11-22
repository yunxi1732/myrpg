using System;

public static class MainMenuRuntimeRegistry
{
    private static Func<UI_MainMenu, IMainMenuRuntime> factory;

    public static bool IsRegistered => factory != null;

    public static void Register(Func<UI_MainMenu, IMainMenuRuntime> create)
    {
        factory = create ?? throw new ArgumentNullException(nameof(create));
    }

    public static IMainMenuRuntime Create(UI_MainMenu menu) =>
        factory != null ? factory(menu) : throw new InvalidOperationException("Menu runtime is not registered.");
}
