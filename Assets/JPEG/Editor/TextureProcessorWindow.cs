using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class TextureProcessorWindow : EditorWindow
{
    private List<Texture2D> texturesToProcess = new List<Texture2D>();
    private int currentIndex = 0;

    // --- Encoding Settings ---
    private int quality = 75;
    private string suffix = "_jpg";
    private bool downsampleChroma = true; 
    private bool optimalHuffman = true;

    // --- Path Settings ---
    private string overrideFolderPath = "";

    private string GetPathForTexture(Texture2D texture)
    {
        // Check if an override path exists
        if (!string.IsNullOrEmpty(overrideFolderPath))
        {
            return $"{overrideFolderPath}/{texture.name}{suffix}.asset";
        }
        
        // Use path of source asset
        // Fallback to "Assets" if it's somehow not saved on disk yet
        string originalPath = AssetDatabase.GetAssetPath(texture);
        string originalFolder = string.IsNullOrEmpty(originalPath) 
            ? "Assets" : Path.GetDirectoryName(originalPath).Replace("\\", "/");
        
        return $"{originalFolder}/{texture.name}{suffix}.asset";
    }
    
    private void OnGUI()
    {
        if (texturesToProcess.Count == 0 || currentIndex >= texturesToProcess.Count)
        {
            EditorGUILayout.HelpBox("No textures loaded or processing complete.", MessageType.Info);
            if (GUILayout.Button("Close")) 
                Close();
            return;
        }

        Texture2D currentTex = texturesToProcess[currentIndex];

        // --- Header Info ---
        EditorGUILayout.LabelField($"Processing {currentIndex + 1} of {texturesToProcess.Count}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Current Asset: {currentTex.name}");
        EditorGUILayout.Space(10);

        // --- Image Preview ---
        Rect textureRect = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawPreviewTexture(textureRect, currentTex, null, ScaleMode.ScaleToFit);

        EditorGUILayout.Space(10);

        // --- Settings ---
        EditorGUILayout.LabelField("Encoder Parameters", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");
        
        quality = EditorGUILayout.IntSlider("Quality Setting", quality, 1, 100);
        suffix = EditorGUILayout.TextField("Name Suffix", suffix);
        downsampleChroma = EditorGUILayout.Toggle("Downsample Chroma", downsampleChroma);
        optimalHuffman = EditorGUILayout.Toggle("Optimal Huffman", optimalHuffman);
        
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // --- Output Path UI ---
        EditorGUILayout.LabelField("Output Destination", EditorStyles.boldLabel);
        
        string previewPath = GetPathForTexture(currentTex);

        // 2. Draw the single-line UI
        EditorGUILayout.BeginHorizontal();

        // Read-only text box for the preview
        EditorGUILayout.LabelField(previewPath);

        // Folder selection button
        if (GUILayout.Button("Folder...", GUILayout.Width(65)))
        {
            // Start the folder browser in the current active folder
            string startDir = Path.GetDirectoryName(previewPath).Replace("\\", "/");
            string absPath = EditorUtility.OpenFolderPanel("Select Output Folder", startDir, "");

            if (!string.IsNullOrEmpty(absPath))
            {
                // Ensure the selected folder is inside the Unity Project's Assets folder
                if (absPath.StartsWith(Application.dataPath))
                {
                    // Convert absolute path to project-relative path
                    overrideFolderPath = "Assets" + absPath.Substring(Application.dataPath.Length);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "The output folder must be located inside the project's Assets directory.", "OK");
                }
            }
        }

        // Clear override button (greyed out if no override is set)
        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(overrideFolderPath));
        if (GUILayout.Button("X", GUILayout.Width(25)))
        {
            overrideFolderPath = string.Empty;
            GUI.FocusControl(null); // Drops focus so the UI updates immediately
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);

        // --- Navigation Buttons ---
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Next Image", GUILayout.Height(30)))
        {
            if (MoveToNext() == false)
                Close();
        }

        if (GUILayout.Button("Process All", GUILayout.Height(30)))
        {
            while (MoveToNext())
                continue;
            
            Close();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void CreateAsset(Texture2D texture)
    {
        if (JpegHelpers.IsValidTexture(texture) == false)
        {
            Debug.LogWarning("Something went wrong, texture is not valid: " + texture.name);
            return;
        }
        
        JpegAsset jpegAsset = ScriptableObject.CreateInstance<JpegAsset>();
        jpegAsset.hideFlags = HideFlags.NotEditable;
        if (JpegHelpers.IsReadableTexture(texture))
        {
            jpegAsset.jpeg = new(texture, downsampleChroma, quality, optimalHuffman);
        }
        else
        {
            Texture2D readableTexture = JpegHelpers.GetReadableTexture(texture);
            jpegAsset.jpeg = new(readableTexture, downsampleChroma, quality, optimalHuffman);
            DestroyImmediate(readableTexture);
        }
        
        string outputPath = GetPathForTexture(texture);
        outputPath = AssetDatabase.GenerateUniqueAssetPath(outputPath);
        AssetDatabase.CreateAsset(jpegAsset, outputPath);
        
        EditorUtility.SetDirty(jpegAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Debug.Log("Created JPEG Buffer: " + outputPath);
    }

    private bool MoveToNext()
    {
        CreateAsset(texturesToProcess[currentIndex]);
        
        if (++currentIndex >= texturesToProcess.Count)
        {
            EditorUtility.DisplayDialog("Done!", "All textures have been processed.", "OK");
            return false;
        }

        return true;
    }

    [MenuItem("MyTools/Process Selected Textures")]
    public static void OpenWindow()
    {
        List<Texture2D> selectedTextures = new();
        
        // check if at least one asset is a Texture2D
        foreach (Object obj in Selection.objects)
        {
            if (obj is not Texture2D texture)
                continue;

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
                continue;

            if (JpegHelpers.IsValidTexture(texture))
            {
                selectedTextures.Add(texture);
            }
        }

        if (selectedTextures.Count == 0)
        {
            EditorUtility.DisplayDialog("No Textures", "Please select at least one valid Texture2D asset.", "OK");
            return;
        }

        TextureProcessorWindow window = GetWindow<TextureProcessorWindow>("Texture Processor");
        window.texturesToProcess = selectedTextures;
        window.currentIndex = 0;
        window.Show();
    }

    [MenuItem("MyTools/Process Selected Textures", true)]
    public static bool ValidateOpenWindow()
    {
        // check if at least one asset is a Texture2D
        foreach (Object obj in Selection.objects)
        {
            if (obj is not Texture2D texture)
                continue;

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
                continue;

            if (JpegHelpers.IsValidTexture(texture))
            {
                return true;
            }
        }

        return false;
    }
}