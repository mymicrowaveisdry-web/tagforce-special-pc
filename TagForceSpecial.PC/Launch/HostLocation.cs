using Godot;
using TagForceSpecial.PC.Setup;

namespace TagForceSpecial.PC.Launch;

/// <summary>Where this Godot process and its bundled Workbench are, whether exported or run by the editor from the project.</summary>
internal static class HostLocation
{
	private static readonly string WorkbenchFile = System.IO.Path.Combine("workbench", "TagForceSpecial.Workbench.exe");

	/// <summary>Running from an export template rather than the editor binary.</summary>
	public static bool Exported => OS.HasFeature("template");

	/// <summary>How to start this host again: the exported executable, or the editor binary pointed at this project.</summary>
	public static GameHost Self() => Exported ? new(OS.GetExecutablePath(), []) : new(OS.GetExecutablePath(), ["--path", ProjectRoot()]);

	/// <summary>The Workbench beside an exported game, or the copy the build bundles into the project's .NET output.</summary>
    public static string Workbench() => Exported
        ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, WorkbenchFile)
        : System.IO.Path.Combine(ProjectRoot(), ".godot", "mono", "temp", "bin", OS.IsDebugBuild() ? "Debug" : "Release", WorkbenchFile);

    private static string ProjectRoot() => System.IO.Path.TrimEndingDirectorySeparator(ProjectSettings.GlobalizePath("res://"));
}
