using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

public class JpegProcessorWindow : EditorWindow
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

    // --- Preview Settings ---
    private static ComputeShader previewCompute;
    private GraphicsBuffer quantBuffer;
    private RenderTexture previewTexture;
    private Texture2D previewTarget;
    private int previewQuality;
    private bool previewDownsample;

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
            EditorGUILayout.HelpBox("Error no textures loaded or processing complete.", MessageType.Info);
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
        // only update texture when it changes
        if (previewTarget != currentTex || previewQuality != quality || previewDownsample != downsampleChroma)
        {
            UpdatePreview(currentTex);
        }
        Rect textureRect = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawPreviewTexture(textureRect, previewTexture, null, ScaleMode.ScaleToFit);

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
            MoveToNext();
        }

        if (GUILayout.Button($"Process All ({texturesToProcess.Count - currentIndex})", GUILayout.Height(30)))
        {
            while (MoveToNext()) ;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void UpdatePreview(Texture2D target)
    {
        GraphicsFormat format = GraphicsFormatUtility.GetComponentCount(target.format) == 1 
            ? GraphicsFormat.R8_UNorm 
            : GraphicsFormat.R8G8B8A8_UNorm;

        if (target.isDataSRGB)
            format = GraphicsFormatUtility.GetSRGBFormat(format);
        
        // recreate texture if needed
        if (previewTexture == null)
        {
            previewTexture = new RenderTexture(target.width, target.height, 0, format);
            previewTexture.enableRandomWrite = true;
        }
        else if (previewTexture.width != target.width || previewTexture.height != target.height || format != previewTexture.graphicsFormat)
        {
            previewTexture.Release();
            previewTexture = new RenderTexture(target.width, target.height, 0, format);
            previewTexture.enableRandomWrite = true;
        }
        
        var tableLuminance = JpegData.CreateScaledQuantTables(MCUBlock.QuantizationTable, quality);
        float[] tempBuffer = new float[64];
        for (int i = 0; i < 64; i++)
        {
            tempBuffer[i] = (float)tableLuminance[i];
        }
        quantBuffer.SetData(tempBuffer, 0, 0, 64);
        var tableChroma = JpegData.CreateScaledQuantTables(MCUBlock.highCompressionLumaQuant, quality);
        for (int i = 0; i < 64; i++)
        {
            tempBuffer[i] = (float)tableChroma[i];
        }
        quantBuffer.SetData(tempBuffer, 0, 64, 64);
        
        int kernelIndex = 0;
        int mcuWidth = 8;
        int mcuHeight = 8;
        previewCompute.SetBuffer(kernelIndex, "_QuantTables", quantBuffer);
        previewCompute.SetTexture(kernelIndex, "_InputTex", target);
        previewCompute.SetTexture(kernelIndex, "_OutputTex", previewTexture);
        previewCompute.SetInt("_ImageWidth", previewTexture.width);
        previewCompute.SetInt("_ImageHeight", previewTexture.height);
        previewCompute.SetBool("_IsSRGB", target.isDataSRGB);
        previewCompute.Dispatch(kernelIndex, 
            JpegHelpers.DivRoundUp(previewTexture.width, mcuWidth), 
            JpegHelpers.DivRoundUp(previewTexture.height, mcuHeight),
            1);

        previewTarget = target;
        previewQuality = quality;
        previewDownsample = downsampleChroma;
    }

    private void OnEnable()
    {
        if (previewCompute == null)
            previewCompute = AssetDatabase.LoadAssetByGUID<ComputeShader>(new GUID("e1d40a76918f1a44a936eda60c3eea39"));

        quantBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 64 * 3, sizeof(float));
    }

    private void OnSelectionChange()
    {
        List<Texture2D> selectedTextures = new();
        
        // check if at least one asset is a Texture2D
        foreach (Object obj in Selection.objects)
        {
            if (obj is not Texture2D texture)
                continue;

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
                continue;

            if (JpegHelpers.IsValidForJPEG(texture))
            {
                selectedTextures.Add(texture);
            }
        }
        
        texturesToProcess = selectedTextures;
        currentIndex = 0;
        
        Repaint();
    }

    private void OnDisable()
    {
        if (quantBuffer != null)
        {
            quantBuffer.Release();
            quantBuffer = null;
        }
        
        if (previewTexture != null)
        {
            previewTexture.Release();
            quantBuffer = null;
        }
    }

    private void CreateAsset(Texture2D texture)
    {
        if (JpegHelpers.IsValidForJPEG(texture) == false)
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
            return false;
        }

        return true;
    }

    [MenuItem("Window/mattdevv/Convert Textures to JPEG")]
    public static void OpenWindow()
    {
        JpegProcessorWindow window = GetWindow<JpegProcessorWindow>("Texture Processor");
        window.texturesToProcess = new List<Texture2D>();
        window.currentIndex = 0;
        window.Show();
    }
}