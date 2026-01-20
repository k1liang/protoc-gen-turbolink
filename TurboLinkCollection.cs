using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Text.Json;
using Google.Protobuf.Compiler;
using Google.Protobuf.Reflection;
using Google.Protobuf.Collections;

namespace protoc_gen_turbolink
{
	public class GrpcEnumField
	{
		public string Name { get; set; }				//eg. "Male", "Female"
		public int Number { get; set; }					//eg. "0", "1"
	}
	public class GrpcEnum
	{
		public string Name { get; set; }                //eg. "EGrpcCommonGender"
		public string DisplayName { get; set; }         //eg. "Common.Gender"
		public string OriginalDisplayName { get; set; }         //eg. "Common.Gender"
		public List<GrpcEnumField> Fields { get; set; }
		public bool MissingZeroField = false;
	}

	abstract public class GrpcMessageField
	{
		public readonly FieldDescriptorProto FieldDesc;
		public GrpcMessageField(FieldDescriptorProto fieldDesc)
		{
			FieldDesc = fieldDesc;
			NeedNativeMake = false;
			if (fieldDesc != null)
			{
				FieldGrpcName = fieldDesc.Name.ToLower();
				if(TurboLinkUtils.CppKeyWords.Contains(FieldGrpcName))
				{
					//add underline if grpc name same as any cpp keywords
					FieldGrpcName += "_";
				}
			}
			FieldDefaultValue = string.Empty;
		}
		public abstract string FieldType						//eg. "int32", "FString", "EGrpcCommonGender", "TArray<FGrpcUserRegisterRequestAddress>"
		{
			get;
		}
		public virtual string FieldGrpcType						//eg. "::Common::Gender", "::google::protobuf::Value"
		{
			get => FieldDesc.TypeName.Replace(".", "::");
		}
		public virtual string FieldName							//eg. "Age", "MyName", "Gender", "AddressArray"
		{
			get => TurboLinkUtils.GetMessageFieldName(FieldDesc);
		}
		public string FieldGrpcName { get; set; }				//eg. "age", "my_name", "gender", "address_array"
		public abstract string TypeAsNativeField                //eg. "TSharedPtr<FGrpcUserRegisterRequestAddress>", "TArray<TSharedPtr<FGrpcUserRegisterRequestAddress>>"
		{
			get;
		}
		public string FieldDefaultValue { get; set; }   //eg. "=0", '=""', "= static_cast<EGrpcCommonGender>(0)", ""
		public bool NeedNativeMake { get; set; }
	}
	public class GrpcMessageField_Single : GrpcMessageField
	{
		public GrpcMessageField_Single(FieldDescriptorProto fieldDesc) : base(fieldDesc)
		{
			FieldDefaultValue = TurboLinkUtils.GetFieldDefaultValue(FieldDesc, FieldDesc.HasDefaultValue ? FieldDesc.DefaultValue : null);
		}
		public override string FieldType
		{
			get => TurboLinkUtils.GetFieldType(FieldDesc);
		}
		public override string TypeAsNativeField
		{
			get => NeedNativeMake ? ("TSharedPtr<" + TurboLinkUtils.GetFieldType(FieldDesc) + ">") : FieldType;
		}
	}
	public class GrpcMessageField_Repeated : GrpcMessageField
	{
		public readonly GrpcMessageField ItemField;
		public GrpcMessageField_Repeated(FieldDescriptorProto fieldDesc) : base(fieldDesc)
		{
			ItemField = new GrpcMessageField_Single(fieldDesc);
		}
		public override string FieldType
		{
			get => "TArray<" + ItemField.FieldType + ">";
		}
		public override string TypeAsNativeField
		{
			get => NeedNativeMake ? ("TArray<TSharedPtr<" + ItemField.FieldType + ">>") : FieldType;
		}
	}
	public class GrpcMessageField_Map : GrpcMessageField
	{
		public readonly GrpcMessageField KeyField;
		public readonly GrpcMessageField ValueField;
		public GrpcMessageField_Map(FieldDescriptorProto fieldDesc, FieldDescriptorProto keyField, FieldDescriptorProto valueField) : base(fieldDesc)
		{
			KeyField = new GrpcMessageField_Single(keyField);
			ValueField = new GrpcMessageField_Single(valueField);
		}
		public override string FieldType
		{
			get => "TMap<" + KeyField.FieldType + ", " + ValueField.FieldType + ">";
		}
		public override string TypeAsNativeField
		{
			get => NeedNativeMake ? ("TMap<" + KeyField.FieldType + ", TSharedPtr<" + ValueField.FieldType + ">>") : FieldType;
		}
	}
	public class GrpcMessageField_Oneof : GrpcMessageField
	{
		public readonly GrpcMessage_Oneof OneofMessage;
		public GrpcMessageField_Oneof(GrpcMessage_Oneof oneofMessage) : base(null)
		{
			OneofMessage = oneofMessage;
		}
		public override string FieldType
		{
			get => OneofMessage.Name;
		}
		public override string FieldGrpcType
		{
			get => string.Empty;	//should not be called!
		}
		public override string FieldName
		{
			get => OneofMessage.CamelName;
		}
		public override string TypeAsNativeField
		{
			get => string.Empty;	//should not be called!
		}
	}

	public class GrpcMessage
	{
		public readonly DescriptorProto MessageDesc;
		public readonly GrpcServiceFile ServiceFile;
		public GrpcMessage(DescriptorProto messageDesc, GrpcServiceFile serviceFile)
		{
			MessageDesc = messageDesc;
			ServiceFile = serviceFile;
			Fields = new List<GrpcMessageField>();
			HasNativeMake = false;
		}
		public int Index { get; set; }
		public virtual string Name                               //eg. "FGrpcGreeterHelloResponse",  "FGrpcGoogleProtobufValue"
		{
			get => "FGrpc" +
				ServiceFile.CamelPackageName +
				TurboLinkUtils.JoinCamelString(ParentMessageNameList, string.Empty) +
				CamelName;
		}
		public virtual string CamelName							//eg. "HelloResponse", "Value"
		{
			get => TurboLinkUtils.MakeCamelString(MessageDesc.Name);
		}
		public virtual string GrpcName                           //eg. "Greeter::HelloResponse", "google::protobuf::Value"
		{
			get => ServiceFile.GrpcPackageName + "::" +
				TurboLinkUtils.JoinString(ParentMessageNameList, "::") +
				MessageDesc.Name;
		}
		public virtual string DisplayName						//eg. "Greeter.HelloResponse", "GoogleProtobuf.Value"
		{
			get => ServiceFile.CamelPackageName + "." +
				TurboLinkUtils.JoinCamelString(ParentMessageNameList, ".") +
				CamelName;
		}

		public virtual string OriginalDisplayName
		{
			get => ServiceFile.PackageOriginalName + "." +
			       string.Join(".", ParentMessageNameList) + 
			       MessageDesc.Name;
		}
		public string[] ParentMessageNameList;
		public List<GrpcMessageField> Fields { get; set; }
		public bool HasNativeMake { get; set; }		
	}
	public class GrpcMessage_Oneof : GrpcMessage
	{
		public readonly OneofDescriptorProto OneofDesc;
		public readonly GrpcMessage ParentMessage;
		public readonly GrpcEnum OneofEnum;
		public GrpcMessage_Oneof(OneofDescriptorProto oneofDesc, GrpcMessage parentMessage, GrpcEnum oneofEnum) : base(null, parentMessage.ServiceFile)
		{
			OneofDesc = oneofDesc;
			ParentMessage = parentMessage;
			OneofEnum = oneofEnum;
		}
		public override string Name								//eg. "FGrpcGoogleProtobufValueKind"
		{
			get => ParentMessage.Name + CamelName;
		}
		public override string CamelName                         //eg. "Kind"
		{
			get => TurboLinkUtils.MakeCamelString(OneofDesc.Name);
		}
		public override string GrpcName							//eg. "kind"
		{
			get => OneofDesc.Name;
		}
		public override string DisplayName						//eg. "GoogleProtobuf.Value.Kind"
		{
			get => ParentMessage.DisplayName + "." + CamelName;
		}

		public override string OriginalDisplayName
		{
			get => ParentMessage.OriginalDisplayName + "." + OneofDesc.Name;
		}
	}
	public class GrpcServiceMethod
	{
		public readonly MethodDescriptorProto MethodDesc;
		public GrpcServiceMethod(MethodDescriptorProto methodDesc)
		{
			MethodDesc = methodDesc;
		}
		public string Name
		{
			get => MethodDesc.Name;
		}
		public bool ClientStreaming
		{
			get => MethodDesc.ClientStreaming;
		}
		public bool ServerStreaming
		{
			get => MethodDesc.ServerStreaming;
		}
		public string InputType                             //eg. "FGrpcUserRegisterRequest"
		{
			get => TurboLinkUtils.GetMessageName(MethodDesc.InputType);
		}
		public string GrpcInputType                         //eg. "::User::RegisterRequest"
		{
			get => MethodDesc.InputType.Replace(".", "::");
		}
		public string OutputType                            //eg. "FGrpcUserRegisterResponse"
		{
			get => TurboLinkUtils.GetMessageName(MethodDesc.OutputType);
		}
		public string GrpcOutputType                        //eg. "::User::RegisterResponse"
		{
			get => MethodDesc.OutputType.Replace(".", "::");
		}
		public string ContextSuperClass                     //eg. "GrpcContext_Ping_Pong", "GrpcContext_Ping_Stream"
		{
			get => TurboLinkUtils.GetContextSuperClass(MethodDesc);
		}
	}
	public class GrpcService
	{
		public readonly ServiceDescriptorProto ServiceDesc;
		public string Name                                      //eg. "UserService"
		{
			get => ServiceDesc.Name;
		}
		public List<GrpcServiceMethod> MethodArray { get; set; }

		public GrpcService(ServiceDescriptorProto serviceDesc)
		{
			ServiceDesc = serviceDesc;
		}
	}
	public class GrpcServiceFile
	{
		public readonly FileDescriptorProto ProtoFileDesc;
		//split package name as string array
		public readonly string[] PackageNameAsList;
		public string FileName								//eg. "hello.proto", "google/protobuf/struct.proto"
		{
			get => ProtoFileDesc.Name;
		}
		public string CamelFileName                         //eg. "Hello", "Struct"
		{
			get => TurboLinkUtils.GetCamelFileName(FileName);
		}
		public string PackageName                           //eg. "Greeter", "google.protobuf"
		{
			get => ProtoFileDesc.Package;
		}
		public string CamelPackageName                      //eg. "Greeter", "GoogleProtobuf"
		{
			get => string.Join(string.Empty, TurboLinkUtils.MakeCamelStringArray(PackageNameAsList));
		}
		public string PackageOriginalName
		{
			get => string.Join(".", PackageNameAsList);
		}
		public string GrpcPackageName                       //eg. "Greeter", "google::protobuf"
		{
			get => string.Join("::", PackageNameAsList);
		}
		public string TurboLinkBasicFileName                //eg. "SGreeter/Hello", "SGoogleProtobuf/Struct"
		{
			get => "S" + CamelPackageName + "/" + CamelFileName;
		}

		public List<GrpcServiceFile> DependencyFiles { get; set; }
		public List<GrpcEnum> EnumArray { get; set; }
		public List<GrpcMessage> MessageArray { get; set; }
		public List<GrpcService> ServiceArray { get; set; }
		public Dictionary<string, int> Message2IndexMap { get; set; }
		public int GetTotalPingPongMethodCounts()
		{
			int totalPingPongMethodCounts = 0;
			foreach (GrpcService service in ServiceArray)
			{
				foreach (GrpcServiceMethod method in service.MethodArray)
				{
					if (!method.ClientStreaming && !method.ServerStreaming) totalPingPongMethodCounts++;
				}
			}
			return totalPingPongMethodCounts;
		}
		public bool NeedBlueprintFunctionLibrary()
		{
			foreach (GrpcMessage message in MessageArray)
			{
				if (message.HasNativeMake || message is GrpcMessage_Oneof)
				{
					return true;
				}
			}
			return false;
		}
		public GrpcServiceFile(FileDescriptorProto protoFileDesc)
		{
			ProtoFileDesc = protoFileDesc;
			PackageNameAsList = PackageName.Split('.').ToArray();
			Message2IndexMap = new Dictionary<string, int>();
		}
	}
	public class TurboLinkCollection
	{
		public static TurboLinkCollection Instance = new TurboLinkCollection();
		public string InputFileNames;
		//key=ProtoFileName
		public Dictionary<string, GrpcServiceFile> GrpcServiceFiles = new Dictionary<string, GrpcServiceFile>();
		public Dictionary<GrpcServiceFile, string> filePathToService = new Dictionary<GrpcServiceFile, string>();

		public bool AnalysisServiceFiles(CodeGeneratorRequest request, out string error)
		{
			error = null;

			StringBuilder inputFileNames = new StringBuilder();

			//step 1: gather service information
			foreach (FileDescriptorProto protoFile in request.ProtoFile)
			{
				var sf = new GrpcServiceFile(protoFile);
				GrpcServiceFiles.Add(protoFile.Name, sf);
				filePathToService.Add(sf, protoFile.Name);
				inputFileNames.Insert(0, System.IO.Path.GetFileNameWithoutExtension(protoFile.Name) + "_");
			}
			InputFileNames = inputFileNames.ToString();

			//step 2: imported proto files
			foreach (string protoFileName in GrpcServiceFiles.Keys.ToList())
			{
				AddDependencyFiles(protoFileName);
			}

			//setp 3: enum (include nested enum)
			foreach (string protoFileName in GrpcServiceFiles.Keys.ToList())
			{
				AddEnums(protoFileName);
			}

			//step 4: message(include nested message and oneof message)
			foreach (string protoFileName in GrpcServiceFiles.Keys.ToList())
			{
				AddMessages(protoFileName);
			}
			//step 5: service
			foreach (string protoFileName in GrpcServiceFiles.Keys.ToList())
			{
				AddServices(protoFileName);
			}
			//step 6: scan message field to analyze the interdependencies between messages
			foreach (string protoFileName in GrpcServiceFiles.Keys.ToList())
			{
				AnalyzeMessage(protoFileName);
			}

			return true;
		}
		private void AddDependencyFiles(string protoFileName)
		{
			var serviceFile = GrpcServiceFiles[protoFileName];

			serviceFile.DependencyFiles = new List<GrpcServiceFile>();
			foreach (string dependency in serviceFile.ProtoFileDesc.Dependency)
			{
				//set dependency file as turbolink base name, eg. "SGoogleProtobuf/Struct"
				serviceFile.DependencyFiles.Add(GrpcServiceFiles[dependency]);
			}
			GrpcServiceFiles[protoFileName] = serviceFile;
		}
		private void AddEnums(string protoFileName)
		{
			var serviceFile = GrpcServiceFiles[protoFileName];
			serviceFile.EnumArray = new List<GrpcEnum>();

			string[] parentNameList = new string[] { };

			//iterate enum in protofile
			foreach (EnumDescriptorProto enumDesc in serviceFile.ProtoFileDesc.EnumType)
			{
				AddEnum(ref serviceFile, parentNameList, enumDesc);
			}

			//iterate nested enum in message
			foreach (DescriptorProto message in serviceFile.ProtoFileDesc.MessageType)
			{
				AddNestedEnums(ref serviceFile, parentNameList, message);
			}

			GrpcServiceFiles[protoFileName] = serviceFile;
		}
		private void AddNestedEnums(ref GrpcServiceFile serviceFile, string[] parentNameList, DescriptorProto message)
		{
			string[] currentNameList = new string[parentNameList.Length + 1];
			parentNameList.CopyTo(currentNameList, 0);
			currentNameList[parentNameList.Length] = message.Name;

			foreach (EnumDescriptorProto enumDesc in message.EnumType)
			{
				AddEnum(ref serviceFile, currentNameList, enumDesc);
			}

			if (message.NestedType.Count > 0)
			{
				foreach (DescriptorProto nestedProtoMessage in message.NestedType)
				{
					if (nestedProtoMessage.Options != null && nestedProtoMessage.Options.MapEntry) continue;
					AddNestedEnums(ref serviceFile, currentNameList, nestedProtoMessage);
				}
			}
		}
		private void AddEnum(ref GrpcServiceFile serviceFile, string[] parentNameList, EnumDescriptorProto enumDesc)
		{
			GrpcEnum newEnum = new GrpcEnum();
			newEnum.Name = string.Join(string.Empty,
				"EGrpc", serviceFile.CamelPackageName, TurboLinkUtils.JoinString(parentNameList, string.Empty), enumDesc.Name);

			newEnum.DisplayName = serviceFile.CamelPackageName + "." + 
				TurboLinkUtils.JoinString(parentNameList, ".") + 
				enumDesc.Name;
			newEnum.OriginalDisplayName = serviceFile.PackageOriginalName + "." + 
			                              TurboLinkUtils.JoinString(parentNameList, ".") + 
			                              enumDesc.Name;

			newEnum.Fields = new List<GrpcEnumField>();
			bool missingZeroField = true;
			foreach (EnumValueDescriptorProto enumValue in enumDesc.Value)
			{
				GrpcEnumField newEnumField = new GrpcEnumField();
				newEnumField.Name = enumValue.Name;
				newEnumField.Number = enumValue.Number;
				newEnum.Fields.Add(newEnumField);
				if (enumValue.Number == 0) missingZeroField = false;
			}
			newEnum.MissingZeroField = missingZeroField;
			serviceFile.EnumArray.Add(newEnum);
		}
		private void AddMessages(string protoFileName)
		{
			var serviceFile = GrpcServiceFiles[protoFileName];
			serviceFile.MessageArray = new List<GrpcMessage>();

			string[] parentNameList = new string[] { };
			foreach (DescriptorProto protoMessage in serviceFile.ProtoFileDesc.MessageType)
			{
				AddMessage(ref serviceFile, parentNameList, protoMessage);
			}
			GrpcServiceFiles[protoFileName] = serviceFile;
		}
		private void AddMessage(ref GrpcServiceFile serviceFile, string[] parentMessageNameList, DescriptorProto protoMessage)
		{
			GrpcMessage message = new GrpcMessage(protoMessage, serviceFile);
			message.ParentMessageNameList = parentMessageNameList;

			//add nested message 
			if (protoMessage.NestedType.Count > 0)
			{
				// Console.WriteLine($"AddMessage3 begin .................");
				string[] currentMessageNameList = new string[parentMessageNameList.Length + 1];
				parentMessageNameList.CopyTo(currentMessageNameList, 0);
				currentMessageNameList[parentMessageNameList.Length] = protoMessage.Name;
				
				foreach (DescriptorProto nestedProtoMessage in protoMessage.NestedType)
				{
					if (nestedProtoMessage.Options != null && nestedProtoMessage.Options.MapEntry) continue;
					AddMessage(ref serviceFile, currentMessageNameList, nestedProtoMessage);
				}
				// Console.WriteLine($"AddMessage3 end .................");
			}

			//add oneof message
			//key=oneof message index in parent message, value.1=enum index in service, value.2=message index in service
			Dictionary<int, Tuple<int, int>> oneofMessageMap = new Dictionary<int, Tuple<int, int>>(); 
			if (protoMessage.OneofDecl.Count > 0)
			{
				// Console.WriteLine($"AddMessage2 begin .................");
				for(int i=0; i< protoMessage.OneofDecl.Count; i++)
				{
					// optional float vitamin = 3，编译器生成的 AST 会把 vitamin 放进一个“隐式 oneof”里，这个 oneof 对应 has_vitamin 标志,所以这里要滤掉
					bool isProto3OptionalOneof = protoMessage.OneofDecl[i].Name.StartsWith("_");
					if(isProto3OptionalOneof) continue;
					
					oneofMessageMap.Add(i, new Tuple<int, int>(serviceFile.EnumArray.Count, serviceFile.MessageArray.Count));

					GrpcEnum oneofEnum = new GrpcEnum();

					//add oneof message 
					GrpcMessage_Oneof oneofMessage = new GrpcMessage_Oneof(protoMessage.OneofDecl[i], message, oneofEnum);
					oneofMessage.Index = serviceFile.MessageArray.Count;
					serviceFile.MessageArray.Add(oneofMessage);
					// Console.WriteLine($"AddMessage2 {oneofMessage.Name} in {serviceFile.PackageOriginalName}, oneof: {protoMessage.OneofDecl[i]}");

					//add oneof enum
					oneofEnum.Name = "EGrpc" + oneofMessage.Name.Substring(5);
					oneofEnum.DisplayName = oneofMessage.DisplayName;
					oneofEnum.OriginalDisplayName =  oneofMessage.OriginalDisplayName;
					oneofEnum.Fields = new List<GrpcEnumField>();
					serviceFile.EnumArray.Add(oneofEnum);
				}
				// Console.WriteLine($"AddMessage2 end .................");
			}

			//add message field
			foreach (FieldDescriptorProto field in protoMessage.Field)
			{
				GrpcMessageField messageField = null;
				bool isMapField;
				FieldDescriptorProto keyField, valueField;
				(isMapField, keyField, valueField) = TurboLinkUtils.IsMapField(field, protoMessage);
				if (isMapField)
				{
					messageField = new GrpcMessageField_Map(field, keyField, valueField);
				}
				else if (field.Label == FieldDescriptorProto.Types.Label.Repeated)
				{
					messageField = new GrpcMessageField_Repeated(field);
				}
				else
				{
					messageField = new GrpcMessageField_Single(field);
				}

				if (field.HasOneofIndex && oneofMessageMap.ContainsKey(field.OneofIndex))
				{
					//add enum field
					GrpcEnum oneofEnum = serviceFile.EnumArray[oneofMessageMap[field.OneofIndex].Item1];
					GrpcEnumField oneofEnumField = new GrpcEnumField();
					oneofEnumField.Name = messageField.FieldName;
					oneofEnumField.Number = oneofEnum.Fields.Count;
					oneofEnum.Fields.Add(oneofEnumField);

					//add field to one of message
					GrpcMessage_Oneof oneofMessage = (GrpcMessage_Oneof)serviceFile.MessageArray[oneofMessageMap[field.OneofIndex].Item2];
					if (oneofMessage.Fields.Count == 0)
					{
						//first field of oneof zone, add oneof field to parent message
						GrpcMessageField_Oneof oneofField = new GrpcMessageField_Oneof(oneofMessage);
						message.Fields.Add(oneofField);
					}
					oneofMessage.Fields.Add(messageField);
				}
				else
				{
					message.Fields.Add(messageField);
				}
			}
			message.Index = serviceFile.MessageArray.Count;
			serviceFile.Message2IndexMap.Add(
				"." + serviceFile.PackageName + "." +
				TurboLinkUtils.JoinString(parentMessageNameList, ".") +
				protoMessage.Name,
				message.Index);
			serviceFile.MessageArray.Add(message);
			
			// Console.WriteLine($"AddMessage1 {message.Name} in {serviceFile.PackageOriginalName}, messageIndex: {message.Index}");
		}
		private void AddServices(string protoFileName)
		{
			var serviceFile = GrpcServiceFiles[protoFileName];
			serviceFile.ServiceArray = new List<GrpcService>();

			foreach (ServiceDescriptorProto service in serviceFile.ProtoFileDesc.Service)
			{
				GrpcService newService = new GrpcService(service);
				newService.MethodArray = new List<GrpcServiceMethod>();

				foreach (MethodDescriptorProto method in service.Method)
				{
					newService.MethodArray.Add(new GrpcServiceMethod(method));
				}
				serviceFile.ServiceArray.Add(newService);
			}
			GrpcServiceFiles[protoFileName] = serviceFile;
		}

		private void AnalyzeMessage(string protoFileName)
		{
			var serviceFile = GrpcServiceFiles[protoFileName];
			RebuildMessageIndices(serviceFile);
			
			while (!AnalyzeMessageImp(serviceFile))
			{
			}
		}

		private string GetMessageName(GrpcServiceFile serviceFile, GrpcMessage tmp, string Name)
		{
			return "." + serviceFile.PackageName + "." +
			       TurboLinkUtils.JoinString(tmp.ParentMessageNameList, ".") +
			       Name;
		}

		private void RebuildMessageIndices(GrpcServiceFile serviceFile)
		{
			// rebuild message index map
			serviceFile.Message2IndexMap.Clear();
			for(int i=0; i< serviceFile.MessageArray.Count; i++)
			{
				GrpcMessage tmp = serviceFile.MessageArray[i];
				tmp.Index = i;
								
				if(tmp.MessageDesc != null)
				{
					serviceFile.Message2IndexMap.Add((GetMessageName(serviceFile, tmp, tmp.MessageDesc.Name)), i);		
					// Console.WriteLine($"Message2IndexMap.Add: {GetMessageName(serviceFile, tmp, tmp.MessageDesc.Name)}, index: {i}");
				}
			}
		}
		private bool AnalyzeMessageImp(GrpcServiceFile serviceFile)
		{
			foreach(GrpcMessage message in serviceFile.MessageArray)
			{
				foreach(GrpcMessageField messageField in message.Fields)
				{
					// if (messageField.FieldDesc==null || //Oneof message field
					// 	messageField.FieldDesc.Type != FieldDescriptorProto.Types.Type.Message) continue;
					string typeName = "";
					int foundIndex = -1;
					if (messageField.FieldDesc == null)
					{
						//Oneof message field
						if (messageField is GrpcMessageField_Oneof)
						{
							GrpcMessageField_Oneof oneofMessageField = (GrpcMessageField_Oneof)messageField;
							foundIndex = serviceFile.MessageArray.IndexOf(oneofMessageField.OneofMessage);
						}
						else
						{
							continue;
						}
					}

					if (typeName.Length == 0)
					{
						typeName = messageField.FieldDesc?.TypeName;
					}

					if (messageField is GrpcMessageField_Map)
					{
						//for map field, pick value field name, eg. "map<string, Address>" => "Address"
						GrpcMessageField_Map mapMessageField = (GrpcMessageField_Map)messageField;
						typeName = mapMessageField.ValueField.FieldDesc.TypeName;
					}

					// Console.WriteLine($"type name: {typeName}");
					if ((foundIndex >= 0) || (typeName != null && serviceFile.Message2IndexMap.ContainsKey(typeName)))
					{
						int index = foundIndex >= 0 ? foundIndex: serviceFile.Message2IndexMap[typeName];
						if(index >= message.Index)
						{
							// 记录插入位置和消息，以便后续处理
							// messageField.NeedNativeMake = true;
							// message.HasNativeMake = true;
							
							// 插入新位置，然后重新排序
							var msg = serviceFile.MessageArray[index];
							serviceFile.MessageArray.RemoveAt(index);
							serviceFile.MessageArray.Insert(message.Index, msg);
							
							// rebuild message index map
							RebuildMessageIndices(serviceFile);
							return false;
						}
					}
				}
			}

			return true;
		}
	}
}
