using Google.Protobuf.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

#region 数据结构

public static class CommentTagDefine
{
    public const string AppendCode = "AppendCode";
    public const string Rename = "Rename";
    public const string BlueprintReadWrite = "BlueprintReadWrite";
    public const string DefaultValue = "DefaultValue";
    public const string ExportConvertFunctionImplement = "ExportConvertFunctionImplement";
    public const string RoundToFloat = "RoundToFloat";
    public const string LowerString = "LowerString";
    public const string FName = "FName";

    public const string FValue = "FValue";
    public const string FMapKey = "FMapKey";
    public const string FMapValue = "FMapValue";
    public const string FArrayValue = "FArrayValue";
    public const string FOneofValue = "FOneofFValue";
}

public sealed class TagInfo
{
    public string Tag;
    public string Info;
}

public sealed class FieldInfoEx
{
    public FieldDescriptorProto Descriptor;
    public List<TagInfo> TagInfos = new();
}

public sealed class MessageInfo
{
    public DescriptorProto Descriptor;
    public List<TagInfo> TagInfos = new(); // message 上的 meta
    public Dictionary<FieldDescriptorProto, FieldInfoEx> Fields = new();
}

#endregion

/// <summary>
/// 解析 .proto 文件中的 // meta(...) 注释，并将其与 message / field Descriptor 关联。
/// </summary>
public sealed class ProtoCommentParser
{
    public FileDescriptorProto ProtoFileDesc;
    public static Dictionary<DescriptorProto, MessageInfo> GlobalMessageInfos = new Dictionary<DescriptorProto, MessageInfo>();
    public static Dictionary<FieldDescriptorProto, FieldInfoEx> GlobalFieldInfos = new();

    public static bool FindMessageMeta(DescriptorProto file, string tag, out string info)
    {
        info = "";
        if (GlobalMessageInfos.TryGetValue(file, out var infoArray))
        {
            var temp = infoArray.TagInfos.Find(tmp => tmp.Tag == tag);
            if (temp != null)
            {
                info = temp.Info;
                return true;
            }
        }
        return false;
    }
    public static bool FindFieldMeta(FieldDescriptorProto field, string tag, out string info)
    {
        info = "";
        if (field != null && GlobalFieldInfos.TryGetValue(field, out var infoArray))
        {
            var temp = infoArray.TagInfos.Find(tmp => tmp.Tag == tag);
            if (temp != null)
            {
                info = temp.Info;
                return true;
            }
        }
        return false;
    }
    
    public static bool FindFieldMetaArray(FieldDescriptorProto field, string tag, out List<string> info)
    {
        info = new List<string>();
        if (field != null && GlobalFieldInfos.TryGetValue(field, out var infoArray))
        {
            foreach (var temp in infoArray.TagInfos)
            {
                if (temp.Tag == tag)
                {
                    info.Add(temp.Info);
                }
            }
        }
        return info.Count > 0;
    }
    
    public static string ProcessMetaString(FieldDescriptorProto field, string tag, string inField)
    {
        var ret = inField;
        if (FindFieldMetaArray(field, tag, out var infoList))
        {
            foreach (var tagInfo in infoList)
            {
                if (tagInfo == CommentTagDefine.FName) ret += ".ToString()";
                else if (tagInfo == CommentTagDefine.LowerString) ret += ".ToLower()";
            }
        }

        return ret;
    }
    
    public static bool ContainValueMeta(FieldDescriptorProto field, string tag, string valueTag)
    {
        if (FindFieldMetaArray(field, tag, out var infoList))
        {
            foreach (var tagInfo in infoList)
            {
                if (tagInfo == valueTag) return true;
            }
        }

        return false;
    }

    public ProtoCommentParser(FileDescriptorProto protoFileDesc)
    {
        ProtoFileDesc = protoFileDesc;
    }

    public void ParseAllMessages()
    {
        var sourceInfo = ProtoFileDesc.SourceCodeInfo;
        if (sourceInfo == null)
            return;

        foreach (var loc in sourceInfo.Location)
        {
            if (!string.IsNullOrWhiteSpace(loc.LeadingComments))
            {
                ParseComments(loc, loc.LeadingComments);
            }

            if (!string.IsNullOrWhiteSpace(loc.TrailingComments))
            {
                ParseComments(loc, loc.TrailingComments);
            }
        }
    }

    private void ParseComments(SourceCodeInfo.Types.Location loc, string comments)
    {
        var lines = comments.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("meta", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("// meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tagInfo = ParseMetaTag(line);
            if (tagInfo == null)
                continue;

            var target = FindMessageAndFieldForLocation(ProtoFileDesc, loc);
            if (target == null || target.Message == null)
                continue;

            if (!GlobalMessageInfos.TryGetValue(target.Message, out var msgInfo))
            {
                msgInfo = new MessageInfo
                {
                    Descriptor = target.Message
                };
                GlobalMessageInfos.Add(target.Message, msgInfo);
            }

            if (target.Field == null)
            {
                // message 级 meta
                msgInfo.TagInfos = msgInfo.TagInfos.Concat(tagInfo).ToList();
            }
            else
            {
                // field 级 meta
                if (!msgInfo.Fields.TryGetValue(target.Field, out var fieldInfo))
                {
                    fieldInfo = new FieldInfoEx
                    {
                        Descriptor = target.Field
                    };
                    msgInfo.Fields.Add(target.Field, fieldInfo);
                    GlobalFieldInfos.Add(target.Field, fieldInfo);
                }

                fieldInfo.TagInfos = fieldInfo.TagInfos.Concat(tagInfo).ToList();
            }
        }
    }

    #region meta 行解析

    /// <summary>
    /// 解析单行 meta 注释：
    /// meta(Tag): Value
    /// </summary>
    private static List<TagInfo> ParseMetaTag(string commentLine)
    {
        if (string.IsNullOrWhiteSpace(commentLine))
            return null;

        var trimmed = commentLine.TrimStart();

        if (trimmed.StartsWith("//"))
            trimmed = trimmed.Substring(2).TrimStart();

        var match = Regex.Match(
            trimmed,
            @"^meta\(([^)]+)\)(?:\s*:\s*(.*))?$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        string tag = match.Groups[1].Value.Trim();
        string info = match.Groups[2].Success
            ? match.Groups[2].Value.Trim()
            : string.Empty;

        var arr = info.Split(",");
        var ret = new List<TagInfo>();
        foreach (var tagInfo in arr)
        {
            ret.Add(new TagInfo
            {
                Tag = tag.Trim(),
                Info = tagInfo.Trim()
            });
        }

        return ret;
    }

    #endregion

    #region Location → Message / Field 映射

    private sealed class LocationTarget
    {
        public DescriptorProto Message;
        public FieldDescriptorProto Field; // 可能为 null
    }

    /// <summary>
    /// 根据 SourceCodeInfo.Location.Path 定位到 message 或 field
    /// </summary>
    private static LocationTarget FindMessageAndFieldForLocation(
        FileDescriptorProto fileProto,
        SourceCodeInfo.Types.Location loc)
    {
        var path = loc.Path;
        if (path == null || path.Count < 2)
            return null;

        // 4 = FileDescriptorProto.message_type
        if ((int)path[0] != 4)
            return null;

        int msgIndex = (int)path[1];
        if (msgIndex < 0 || msgIndex >= fileProto.MessageType.Count)
            return null;

        DescriptorProto currentMsg = fileProto.MessageType[msgIndex];

        int i = 2;
        while (i < path.Count)
        {
            int fieldNumber = (int)path[i];
            i++;

            if (i >= path.Count)
                break;

            int index = (int)path[i];
            i++;

            switch (fieldNumber)
            {
                case 3: // DescriptorProto.nested_type
                    if (index < 0 || index >= currentMsg.NestedType.Count)
                        return null;

                    currentMsg = currentMsg.NestedType[index];
                    break;

                case 2: // DescriptorProto.field
                    if (index < 0 || index >= currentMsg.Field.Count)
                        return null;

                    return new LocationTarget
                    {
                        Message = currentMsg,
                        Field = currentMsg.Field[index]
                    };

                default:
                    // enum / oneof / reserved 等，直接认为是 message 级
                    return new LocationTarget
                    {
                        Message = currentMsg,
                        Field = null
                    };
            }
        }

        return new LocationTarget
        {
            Message = currentMsg,
            Field = null
        };
    }

    #endregion
}
