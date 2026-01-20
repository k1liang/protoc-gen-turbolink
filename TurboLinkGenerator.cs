using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.Reflection;

namespace protoc_gen_turbolink
{
    public struct GeneratedFile
    {
        public string FileName;
        public string Content;
        public GrpcServiceFile  ServiceFile;
    }
    public class GenerateParam
    {
        public static GenerateParam Instance = new GenerateParam();
        
        public bool GenerateServiceCode;
        public bool GenerateDefaultFunctionCode;
        public bool GenerateBPHelper;
        public bool SingleOutputFile;
        public string ExportPrefix;
        public string IncludePrefixPath;
    }
    public class TurboLinkGenerator(FileDescriptorProto protoFile, GrpcServiceFile serviceFile)
    {
        public FileDescriptorProto ProtoFile = protoFile;

        public List<GeneratedFile> GeneratedFiles = new List<GeneratedFile>();

        public static Dictionary<GrpcServiceFile, string> UsedFileNames = new Dictionary<GrpcServiceFile, string>();

        public void BuildOutputFiles(GenerateParam generateParam, string removeOutputSubDir)
        {
            GeneratedFile file;
            // string turboLinkBaseName = ServiceFile.TurboLinkBasicFileName;
            string turboLinkBaseName = serviceFile.PackageOriginalName.Replace('.', '/') + "/";
            if (removeOutputSubDir.Length > 0 && turboLinkBaseName.StartsWith(removeOutputSubDir))
            {
                turboLinkBaseName = turboLinkBaseName.Replace(removeOutputSubDir, "");
            }

            // xxxMarshaling.h
            Template.MarshalingH marshalingHTemplate = new Template.MarshalingH(serviceFile, generateParam);
            file = new GeneratedFile();
            file.FileName = string.Join("/", turboLinkBaseName + "Marshaling.h");
            file.Content = marshalingHTemplate.TransformText();
            file.ServiceFile =  serviceFile;
            GeneratedFiles.Add(file);

            // xxxMarshaling.cpp
            Template.MarshalingCPP marshalingCPPTemplate = new Template.MarshalingCPP(serviceFile, generateParam);
            file = new GeneratedFile();
            file.FileName = string.Join("/", turboLinkBaseName + "Marshaling.cpp");
            file.Content = marshalingCPPTemplate.TransformText();
            file.ServiceFile =  serviceFile;
            GeneratedFiles.Add(file);
            
            // xxxMessage.h
            Template.MessageH messageHTemplate = new Template.MessageH(serviceFile, generateParam);
            file = new GeneratedFile();
            file.FileName = string.Join("/", turboLinkBaseName + "Message.h");
            file.Content = messageHTemplate.TransformText();
            file.ServiceFile =  serviceFile;
            GeneratedFiles.Add(file);

            // xxxMessage.cpp
            Template.MessageCPP messageCPPTemplate = new Template.MessageCPP(serviceFile, generateParam);
            file = new GeneratedFile();
            file.FileName = string.Join("/", turboLinkBaseName + "Message.cpp");
            file.Content = messageCPPTemplate.TransformText();
            file.ServiceFile =  serviceFile;
            GeneratedFiles.Add(file);

            if (ProtoFile.Service.Count > 0 && generateParam.GenerateServiceCode)
            {
                // xxxService.h
                Template.ServiceH serviceHTemplate = new Template.ServiceH(serviceFile, generateParam);
                file = new GeneratedFile();
                file.FileName = string.Join("/", turboLinkBaseName + "Service.h");
                file.Content = serviceHTemplate.TransformText();
                file.ServiceFile =  serviceFile;
                GeneratedFiles.Add(file);

                // xxxClient.h
                Template.ClientH clientHTemplate = new Template.ClientH(serviceFile, generateParam);
                file = new GeneratedFile();
                file.FileName = string.Join("/", turboLinkBaseName + "Client.h");
                file.Content = clientHTemplate.TransformText();
                file.ServiceFile =  serviceFile;
                GeneratedFiles.Add(file);

                // xxxClient.cpp
                Template.ClientCPP clientCPPTemplate = new Template.ClientCPP(serviceFile, generateParam);
                file = new GeneratedFile();
                file.FileName = string.Join("/", turboLinkBaseName + "Client.cpp");
                file.Content = clientCPPTemplate.TransformText();
                file.ServiceFile =  serviceFile;
                GeneratedFiles.Add(file);

                // xxxServicePrivate.h
                Template.ServicePrivateH servicePrivateHTemplate = new Template.ServicePrivateH(serviceFile, generateParam);
                file = new GeneratedFile();
                file.FileName = string.Join("/", turboLinkBaseName + "Service_Private.h");
                file.Content = servicePrivateHTemplate.TransformText();
                file.ServiceFile =  serviceFile;
                GeneratedFiles.Add(file);

                // xxxServicePrivate.cpp
                Template.ServicePrivateCPP servicePrivateCPPTemplate = new Template.ServicePrivateCPP(serviceFile, generateParam);
                file = new GeneratedFile();
                file.FileName = string.Join("/", turboLinkBaseName + "Service_Private.cpp");
                file.Content = servicePrivateCPPTemplate.TransformText();
                file.ServiceFile =  serviceFile;
                GeneratedFiles.Add(file);

                // xxxContext.h
                Template.ContextH contextHTemplate = new Template.ContextH(serviceFile, generateParam);
                file = new GeneratedFile();
                file.FileName = string.Join("/", turboLinkBaseName + "Context.h");
                file.Content = contextHTemplate.TransformText();
                file.ServiceFile =  serviceFile;
                GeneratedFiles.Add(file);

                // xxxContext.cpp
                Template.ContextCPP contextCPPTemplate = new Template.ContextCPP(serviceFile, generateParam);
                file = new GeneratedFile();
                file.FileName = string.Join("/", turboLinkBaseName + "Context.cpp");
                file.Content = contextCPPTemplate.TransformText();
                file.ServiceFile =  serviceFile;
                GeneratedFiles.Add(file);

                // xxxService.cpp
                Template.ServiceCPP serviceCPPTemplate = new Template.ServiceCPP(serviceFile, generateParam);
                file = new GeneratedFile();
                file.FileName = string.Join("/", turboLinkBaseName + "Service.cpp");
                file.Content = serviceCPPTemplate.TransformText();
                file.ServiceFile =  serviceFile;
                GeneratedFiles.Add(file);

                // xxxNode.h
                if (serviceFile.GetTotalPingPongMethodCounts() > 0)
                {
                    Template.NodeH nodeHTemplate = new Template.NodeH(serviceFile, generateParam);
                    file = new GeneratedFile();
                    file.FileName = string.Join("/", turboLinkBaseName + "Node.h");
                    file.Content = nodeHTemplate.TransformText();
                    file.ServiceFile =  serviceFile;
                    GeneratedFiles.Add(file);

                    // xxxNode.cpp
                    Template.NodeCPP nodeCPPTemplate = new Template.NodeCPP(serviceFile, generateParam);
                    file = new GeneratedFile();
                    file.FileName = string.Join("/", turboLinkBaseName + "Node.cpp");
                    file.Content = nodeCPPTemplate.TransformText();
                    file.ServiceFile =  serviceFile;
                    GeneratedFiles.Add(file);
                }
            }

            if (generateParam.SingleOutputFile)
            {
                MergeFile(generateParam, turboLinkBaseName, serviceFile);
            }
        }

        private string GetHeaderFileName(string name)
        {
            return name.Replace("bigai.ue.", "").Replace(".", "/");
        }

        enum EVerifyAdditionalType
        {
            None = 0,
            Header,
            Cpp,
        }
        
        private string VerifyFileName(GrpcServiceFile sf, string name, EVerifyAdditionalType additionalType)
        {
            string filePath;
            if (!UsedFileNames.TryGetValue(sf, out filePath))
            {
                // 名字不能重复
                filePath = name;
                var originalFileName = Path.GetFileName(name);
                var fileName = originalFileName; 
                int index = 1;
                var fileList = UsedFileNames.Values.ToList();
                bool found = false;
                do
                {
                    found = false;
                    foreach (var file in fileList)
                    {
                        if (file.EndsWith(fileName))
                        {
                            found = true;
                            break;
                        }
                    }
        
                    if (found)
                    {
                        fileName = $"{originalFileName}_{index++}";
                        filePath = name.Replace(originalFileName, fileName);
                    }
                } while (found);
                
                UsedFileNames.Add(sf, filePath);
            }
        
            // 防止和虚幻文件冲突
            filePath = GenerateParam.Instance.IncludePrefixPath + filePath + "_gen";
            switch (additionalType)
            {
                case EVerifyAdditionalType.Header:
                    return filePath + ".h";
                case EVerifyAdditionalType.Cpp:
                    return filePath + ".cpp";
                default:
                    return filePath;
            }
        }

        private void MergeFile(GenerateParam g, string turboLinkBaseName, GrpcServiceFile s)
        {
            var oldFiles = new List<GeneratedFile>(GeneratedFiles);
            GeneratedFiles.Clear();
            
            var filePath = turboLinkBaseName.EndsWith("/") ?  turboLinkBaseName.Substring(0, turboLinkBaseName.Length - 1) : turboLinkBaseName;
            var file = new GeneratedFile();
            file.FileName = VerifyFileName(s, filePath, EVerifyAdditionalType.Header);
            
            using (var sw = new StringWriter())
            using (var writer = new IndentedTextWriter(sw, "\t"))
            {
                writer.WriteLine("//Generated by TurboLink CodeGenerator, do not edit!");
                writer.WriteLine("#pragma once");
                writer.WriteLine("#include \"TurboLinkGrpcMessage.h\"");                
                foreach (var dependency in s.DependencyFiles)
                {
                    writer.WriteLine($"#include \"{VerifyFileName(dependency, GetHeaderFileName(dependency.PackageOriginalName), EVerifyAdditionalType.Header)}\"");
                }
                
                string generatedHeader = VerifyFileName(s, GetHeaderFileName(s.PackageOriginalName), EVerifyAdditionalType.None) + ".generated.h";
                writer.WriteLine($"#include \"{Path.GetFileName(generatedHeader)}\"");
                writer.WriteLine();
                
                var headerFiles = oldFiles.Where(f => f.FileName.EndsWith(".h"));
                foreach (var temp in headerFiles)
                {
                    var block = VerifyFileName(temp.ServiceFile, temp.FileName.Replace(turboLinkBaseName, ""), EVerifyAdditionalType.None);
                    writer.WriteLine($"// {block}");
                    writer.Write(temp.Content);
                    writer.WriteLine();
                }
                
                file.Content = sw.ToString();
                GeneratedFiles.Add(file);
            }
         
            file = new GeneratedFile();
            file.FileName = VerifyFileName(s, filePath, EVerifyAdditionalType.Cpp);
            
            using (var sw = new StringWriter())
            using (var writer = new IndentedTextWriter(sw, "\t"))
            {
                writer.WriteLine("//Generated by TurboLink CodeGenerator, do not edit!");
                writer.WriteLine($"#include \"{VerifyFileName(s, GetHeaderFileName(s.PackageOriginalName), EVerifyAdditionalType.Header)}\"");
                writer.WriteLine("#include \"google/protobuf/util/json_util.h\"");
                writer.WriteLine();
                
                var cppFiles = oldFiles.Where(f => f.FileName.EndsWith(".cpp"));
                foreach (var temp in cppFiles)
                {
                    var block = VerifyFileName(temp.ServiceFile, temp.FileName.Replace(turboLinkBaseName, ""), EVerifyAdditionalType.None);
                    writer.WriteLine($"// {block}");
                    writer.Write(temp.Content);
                    writer.WriteLine();
                }
                
                file.Content = sw.ToString();
                GeneratedFiles.Add(file);
            }
        }
    }
}
