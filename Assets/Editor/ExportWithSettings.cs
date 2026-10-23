using UnityEditor;

public class ExportWithSettings
{
    [MenuItem("Export/Everything")]
    public static void Export()
    {
        string[] content = { "Assets", "ProjectSettings/TagManager.asset", "ProjectSettings/InputManager.asset" };
        AssetDatabase.ExportPackage(content, "FullProjectMerge.unitypackage", ExportPackageOptions.Interactive | ExportPackageOptions.IncludeDependencies | ExportPackageOptions.Recurse);
    }
}