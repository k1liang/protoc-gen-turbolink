using Google.Protobuf.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

#region 数据结构

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
    public Dictionary<DescriptorProto, MessageInfo> MessageInfos;
    public static Dictionary<FieldDescriptorProto, FieldInfoEx> GlobalFieldInfos = new();

    public ProtoCommentParser(FileDescriptorProto protoFileDesc)
    {
        ProtoFileDesc = protoFileDesc;
    }

    public void ParseAllMessages()
    {
        MessageInfos = new Dictionary<DescriptorProto, MessageInfo>();

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

            if (!MessageInfos.TryGetValue(target.Message, out var msgInfo))
            {
                msgInfo = new MessageInfo
                {
                    Descriptor = target.Message
                };
                MessageInfos.Add(target.Message, msgInfo);
            }

            if (target.Field == null)
            {
                // message 级 meta
                msgInfo.TagInfos.Add(tagInfo);
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

                fieldInfo.TagInfos.Add(tagInfo);
            }
        }
    }

    #region meta 行解析

    /// <summary>
    /// 解析单行 meta 注释：
    /// meta(Tag): Value
    /// </summary>
    private static TagInfo ParseMetaTag(string commentLine)
    {
        if (string.IsNullOrWhiteSpace(commentLine))
            return null;

        var trimmed = commentLine.TrimStart();

        if (trimmed.StartsWith("//"))
            trimmed = trimmed.Substring(2).TrimStart();

        var match = Regex.Match(
            trimmed,
            @"^meta\(([^)]+)\)\s*:\s*(.*)$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        return new TagInfo
        {
            Tag = match.Groups[1].Value.Trim(),
            Info = match.Groups[2].Value.Trim()
        };
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
