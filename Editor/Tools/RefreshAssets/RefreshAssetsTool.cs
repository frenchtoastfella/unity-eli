using System;
using UnityEditor;

namespace UnityEli.Editor.Tools
{
    public class RefreshAssetsTool : IEliTool
    {
        public string Name => "refresh_assets";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var waitForCompilation = JsonHelper.ExtractBool(inputJson, "wait_for_compilation");

            AssetDatabase.Refresh();

            if (!waitForCompilation)
                return ToolResult.Success("Asset database refreshed.");

            if (!EditorApplication.isCompiling)
                return ToolResult.Success("Asset database refreshed. No compilation pending.");

            // Compilation runs on background threads but assembly reload needs the main thread.
            // We cannot block the main thread and wait for reload to finish, so poll for the
            // compilation phase only (background C# compile) with a short timeout.
            var timeout = 30.0;
            var start = EditorApplication.timeSinceStartup;

            while (EditorApplication.isCompiling)
            {
                if (EditorApplication.timeSinceStartup - start > timeout)
                    return ToolResult.Success(
                        "Asset database refreshed. Script compilation is still in progress after 30 seconds. " +
                        "Call refresh_assets again to check if compilation has finished.");

                System.Threading.Thread.Sleep(200);
            }

            return ToolResult.Success("Asset database refreshed and script compilation completed.");
        }
    }
}
