using System;
using System.Collections.Generic;
using System.IO;

public enum ModItemResult
{
    ThumbTexValue_NotFound,
    UnityTexture_NotFound,
    MainAB_NotFound,
    UnityFileLoad_Failed,
    Texture2Image_Failed,
    SaveImage_Failed,
    MoveImage_Failed,
    CSVUpdate_Failed,
    ExtracImage_Success,
    ExtracImage_Finish,
    MoveImage_Finish,
    KeepImage_Finish,
    UnKnown_Error
}

public class ModItem
{
    public string ParentPath { get; private set; }
    public int ParentIndex { get; private set; }
    public Dictionary<string, string> Datas { get; private set; }

    private string[] _lines;
    private string[] _headers;

    private ModItem(string parentPath, int parentIndex, Dictionary<string, string> datas, string[] lines, string[] headers)
    {
        ParentPath = parentPath;
        ParentIndex = parentIndex;
        Datas = datas;
        _lines = lines;
        _headers = headers;
    }

    public static List<ModItem> ReadFromFolder(string folderPath)
    {
        List<ModItem> result = new List<ModItem>();

        var files = Directory.EnumerateFiles(folderPath, "*.csv", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            result.AddRange(ProcessCsvFile(file));
        }

        return result;
    }

    private static List<ModItem> ProcessCsvFile(string filePath)
    {
        List<ModItem> fileItems = new List<ModItem>();
        string[] lines = File.ReadAllLines(filePath);

        if (lines.Length < 5)
            return fileItems;

        string[] headers = lines[3].Split(',');

        for (int i = 4; i < lines.Length; i++)
        {
            string[] values = lines[i].Split(',');

            if (values.Length != headers.Length)
                continue;

            Dictionary<string, string> datas = new Dictionary<string, string>();
            for (int j = 0; j < headers.Length; j++)
            {
                datas[headers[j]] = values[j];
            }
            if (datas.Count != 15) {
                return fileItems;
            }

            fileItems.Add(new ModItem(filePath, i - 3, datas, lines, headers));
        }

        return fileItems;
    }

    public void UpdateValue(string key, string newValue)
    {
        int keyIndex = Array.IndexOf(_headers, key);

        if (keyIndex == -1)
        {
            throw new ArgumentException("The specified key does not exist in the CSV file.", nameof(key));
        }

        if (ParentIndex >= _lines.Length - 3)
        {
            throw new InvalidOperationException("Invalid ParentIndex.");
        }

        string[] values = _lines[ParentIndex + 3].Split(',');

        if (keyIndex >= values.Length)
        {
            throw new InvalidOperationException("The CSV file structure is inconsistent.");
        }

        values[keyIndex] = newValue;
        _lines[ParentIndex + 3] = string.Join(",", values);

        File.WriteAllLines(ParentPath, _lines);

        Datas[key] = newValue;
    }
}