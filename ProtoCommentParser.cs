using Google.Protobuf.Reflection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;


public sealed class TagInfo
{
    public string Tag;
    public string Info;
}

public sealed class MessageInfo
{
    public DescriptorProto Descriptor;
    public List<TagInfo> TagInfos = new();
}

/// <summary>
/// 解析 .proto 文件中的 // meta(...) 注释，并将其与对应的 DescriptorProto 关联。
/// </summary>
public class ProtoCommentParser
{
    public FileDescriptorProto protoFileDesc; 
    public Dictionary<DescriptorProto, MessageInfo> messageInfos;
    
    public ProtoCommentParser(FileDescriptorProto inProtoFileDesc)
    {
        protoFileDesc = inProtoFileDesc;
    }
    
    public void ParseAllMessages()
    {
        messageInfos = new Dictionary<DescriptorProto, MessageInfo>();
        
        var sourceInfo = protoFileDesc.SourceCodeInfo;
        if (sourceInfo == null) return;

        // 遍历每个 Location，Location 里包含 path（指向某个 descriptor）和 leading/trailing comments
        foreach (var loc in sourceInfo.Location)
        {
            // 一般我们关注 leadingComments，但 trailingComments 也做兼容\
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

    private void ParseComments(SourceCodeInfo.Types.Location loc, string comment)
    {
        // 支持多行 comment，这里只需要找到含有“// meta”的那一行
        var lines = comment.Split('\n');
        foreach (var line in lines)
        {
            if (!line.TrimStart().StartsWith("meta", StringComparison.OrdinalIgnoreCase))
                continue;

            // 解析 meta(...) 一行
            var tagInfo = ParseMetaTag(line);
            if (tagInfo == null) continue;

            // 根据 Location.Path 找到对应的 message DescriptorProto
            var descriptor = FindMessageDescriptorForLocation(protoFileDesc, loc);
            if (descriptor == null) continue;

            // 将 TagInfo 挂到对应的 MessageInfo 上
            MessageInfo msgInfo; 
            if (!messageInfos.TryGetValue(descriptor, out msgInfo))
            {
                msgInfo = new MessageInfo
                {
                    Descriptor = descriptor,
                    TagInfos = new List<TagInfo>()
                };
                messageInfos.Add(descriptor, msgInfo);
            }

            msgInfo.TagInfos.Add(tagInfo);
        }
    }

    /// <summary>
    /// 解析单行 meta 注释，例如：
    /// // meta(addfunc): _UESTRUCT& operator=(const FQuaternion& q) { ... }
    /// </summary>
    private static TagInfo ParseMetaTag(string commentLine)
    {
        if (string.IsNullOrWhiteSpace(commentLine))
            return null;

        // 去掉前导的 // 和空白
        var trimmed = commentLine.TrimStart();
        if (trimmed.StartsWith("//"))
            trimmed = trimmed.Substring(2).TrimStart();

        // trimmed 现在形如： meta(addfunc): _UESTRUCT& operator=(...)
        // 使用正则： meta(XXX): YYY
        var match = Regex.Match(trimmed, @"^meta\(([^)]+)\)\s*:\s*(.*)$",
                                RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return new TagInfo
            {
                Tag = match.Groups[1].Value.Trim(),
                Info = match.Groups[2].Value.Trim()
            };
        }

        // 如果格式不满足，不强制抛错，可以选择返回 null 或者做一个更宽松的解析
        return null;
    }

    /// <summary>
    /// 根据 Location.Path 从 FileDescriptorProto 中找到对应的 DescriptorProto（message）。
    /// 仅处理 message / 嵌套 message，忽略 enum / service 等。
    /// </summary>
    private static DescriptorProto FindMessageDescriptorForLocation(
        FileDescriptorProto fileProto,
        SourceCodeInfo.Types.Location loc)
    {
        var path = loc.Path;
        if (path == null || path.Count == 0)
            return null;

        // path[0] 决定顶层对象类型：
        // 4 = FileDescriptorProto.message_type
        // 5 = enum_type
        // 6 = service
        // 7 = extension
        if ((int)path[0] != 4)
            return null; // 非 message，忽略

        if (path.Count < 2)
            return null;

        // 顶层 message
        var msgIndex = (int)path[1];
        if (msgIndex < 0 || msgIndex >= fileProto.MessageType.Count)
            return null;

        var current = fileProto.MessageType[msgIndex];

        // 继续向下解析嵌套 message：
        // 在 DescriptorProto 中：
        //   nested_type 字段号 = 3
        //
        // 嵌套路径模式示意：
        //   [4, topMessageIndex, 3, nestedIndex, 3, nestedIndex2, ...]
        // 我们只要在遇到 fieldNumber == 3 时，跳到对应 NestedType 即可。
        int i = 2;
        while (i < path.Count)
        {
            var fieldNumber = (int)path[i];

            if (fieldNumber != 3)
            {
                // 不是 nested_type，说明 path 指向的是 message 内的字段、枚举等，
                // 但我们只关心所在的 message 本身，所以可以直接返回 current。
                break;
            }

            // 下一个元素是 nested_type 的索引
            i++;
            if (i >= path.Count)
                break;

            var nestedIndex = (int)path[i];
            if (nestedIndex < 0 || nestedIndex >= current.NestedType.Count)
                return null;

            current = current.NestedType[nestedIndex];
            i++;
        }

        return current;
    }
}