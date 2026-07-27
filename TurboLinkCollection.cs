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
using System.Data;
using System.Text.RegularExpressions;

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
			get
			{
				if (ProtoCommentParser.FindFieldMeta(FieldDesc, CommentTagDefine.Rename, out var info))
				{
					return info;
				}
				return TurboLinkUtils.GetMessageFieldName(FieldDesc);
			}
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
			get
			{
				// return "TArray<" + ItemField.FieldType + ">";
				return "TArray<" + TurboLinkUtils.GetFieldType(ItemField.FieldDesc, FieldDesc, CommentTagDefine.FArrayValue) + ">";
			}
		}

		public override string TypeAsNativeField
		{
			get
			{
				// return NeedNativeMake ? ("TArray<TSharedPtr<" + ItemField.FieldType + ">>") : FieldType;
				return NeedNativeMake ? ("TArray<TSharedPtr<" + TurboLinkUtils.GetFieldType(ItemField.FieldDesc, FieldDesc, CommentTagDefine.FArrayValue) + ">>") : FieldType;
			}
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
			get
			{
				// return "TMap<" + KeyField.FieldType + ", " + ValueField.FieldType + ">";
				return "TMap<" 
				       + TurboLinkUtils.GetFieldType(KeyField.FieldDesc, FieldDesc,  CommentTagDefine.FMapKey) 
				       + ", " 
				       + TurboLinkUtils.GetFieldType(ValueField.FieldDesc, FieldDesc,  CommentTagDefine.FMapValue)
				       + ">";
			}
		}

		public override string TypeAsNativeField
		{
			get
			{
				// return NeedNativeMake
				// 	? ("TMap<" + KeyField.FieldType + ", TSharedPtr<" + ValueField.FieldType + ">>")
				// 	: FieldType;
				return NeedNativeMake
					? ("TMap<" + TurboLinkUtils.GetFieldType(KeyField.FieldDesc, FieldDesc,  CommentTagDefine.FMapKey) 
					           + ", TSharedPtr<" + TurboLinkUtils.GetFieldType(ValueField.FieldDesc, FieldDesc,  CommentTagDefine.FMapValue) + ">>")
					: FieldType;
			}
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
		public ProtoCommentParser CommentParser;
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
			
			var protoFileNames = GrpcServiceFiles.Keys.ToList();

			//step 2: imported proto files
			foreach (string protoFileName in protoFileNames)
			{
				AddDependencyFiles(protoFileName);
			}

			//setp 3: enum (include nested enum)
			foreach (string protoFileName in protoFileNames)
			{
				AddEnums(protoFileName);
			}

			//step 4: message(include nested message and oneof message)
			foreach (string protoFileName in protoFileNames)
			{
				AddMessages(protoFileName);
			}
			//step 5: service
			foreach (string protoFileName in protoFileNames)
			{
				AddServices(protoFileName);
			}
			//step 6: scan message field to analyze the interdependencies between messages
			foreach (string protoFileName in protoFileNames)
			{
				AnalyzeMessage(protoFileName);
			}

			//step 7: 解析注释信息
			foreach (string protoFileName in protoFileNames)
			{
				ParseComments(protoFileName);
			}

			// step 8: PODConfig is deliberately narrow. Reject unsupported schemas in the
			// generator instead of emitting a config which only looks trivially copyable.
			if (!ValidatePODConfigs(out error))
			{
				return false;
			}

			return true;
		}

		private bool ValidatePODConfigs(out string error)
		{
			error = null;
			var usedConfigNames = new HashSet<string>();
			foreach (GrpcServiceFile serviceFile in GrpcServiceFiles.Values)
			{
				foreach (GrpcMessage message in serviceFile.MessageArray)
				{
					if (message.MessageDesc == null ||
					    !ProtoCommentParser.FindMessageMeta(message.MessageDesc, CommentTagDefine.PODConfig, out var configName))
					{
						continue;
					}

					if (string.IsNullOrWhiteSpace(configName) ||
					    !Regex.IsMatch(configName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
					{
						error = $"PODConfig on {message.OriginalDisplayName} requires a valid unqualified C++ identifier";
						return false;
					}
					if (!usedConfigNames.Add(configName))
					{
						error = $"duplicate PODConfig C++ name: {configName}";
						return false;
					}

					foreach (FieldDescriptorProto field in message.MessageDesc.Field)
					{
						if (field.Label == FieldDescriptorProto.Types.Label.Repeated ||
						    field.HasOneofIndex ||
						    !TurboLinkUtils.TryGetPODFieldType(field, out _))
						{
							error = $"PODConfig {configName} field {field.Name} must be a singular numeric/bool scalar";
							return false;
						}
					}

					int oneofReferenceCount = serviceFile.MessageArray
						.OfType<GrpcMessage_Oneof>()
						.Sum(oneof => oneof.Fields.Count(field => field.FieldType == message.Name));
					if (oneofReferenceCount != 1)
					{
						error = $"PODConfig {configName} must be referenced by exactly one oneof arm in the same proto file";
						return false;
					}
				}
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

			// Break reference cycles (recursive / mutually-recursive messages) before ordering.
			// UE USTRUCTs are value types and must be defined before use, so the reorder below can
			// never converge on a cycle. For each cycle we wrap one field in TSharedPtr (NeedNativeMake);
			// a pointer needs only a forward declaration, which removes that field's ordering constraint.
			BreakMessageCycles(serviceFile);

			// Reorder so every by-value message dependency is defined before its user.
			// NeedNativeMake fields are skipped now, so the remaining graph is acyclic and this terminates.
			while (!AnalyzeMessageImp(serviceFile))
			{
			}
		}

		// Resolve the same-file message a field references (used for both ordering and cycle analysis).
		// Returns false when the field does not reference a message defined in THIS file (scalars and
		// cross-file references are always fully included, so they impose no local constraint).
		// cuttable = the field may be wrapped in TSharedPtr (NeedNativeMake) to break a cycle; a oneof
		// union pointer is stored inline in its parent and therefore must never be cut.
		private bool TryGetFieldTarget(GrpcServiceFile serviceFile, GrpcMessageField field, out int index, out bool cuttable)
		{
			index = -1;
			cuttable = false;

			if (field is GrpcMessageField_Oneof oneofField)
			{
				index = serviceFile.MessageArray.IndexOf(oneofField.OneofMessage);
				cuttable = false;
				return index >= 0;
			}

			string typeName = field.FieldDesc?.TypeName;
			if (field is GrpcMessageField_Map mapField)
			{
				typeName = mapField.ValueField.FieldDesc.TypeName;
			}

			if (typeName != null && serviceFile.Message2IndexMap.TryGetValue(typeName, out index))
			{
				cuttable = true; //single / repeated / map-value referencing a same-file message
				return true;
			}
			index = -1;
			return false;
		}

		private class MessageEdge
		{
			public GrpcMessage Owner;
			public GrpcMessageField Field;
			public bool Cuttable;
		}

		// Iteratively find one reference cycle and cut a single cuttable edge on it, until acyclic.
		// Preference: cut a repeated/map edge (element-wise, always safe) over a single edge; among
		// equals, cut the deepest (the actual recursion back-edge) so the wrapping stays localized.
		private void BreakMessageCycles(GrpcServiceFile serviceFile)
		{
			int guard = 0;
			int maxIterations = 16;
			foreach (var m in serviceFile.MessageArray) maxIterations += (m.Fields?.Count ?? 0) + 1;

			while (true)
			{
				var cycle = FindOneCycle(serviceFile);
				if (cycle == null) break;

				MessageEdge best = null;
				int bestScore = int.MinValue;
				for (int i = 0; i < cycle.Count; i++)
				{
					var e = cycle[i];
					if (!e.Cuttable) continue;
					int kind = (e.Field is GrpcMessageField_Repeated || e.Field is GrpcMessageField_Map) ? 1 : 0;
					int score = kind * 100000 + i; //deeper edge (larger i) wins ties
					if (score > bestScore)
					{
						bestScore = score;
						best = e;
					}
				}

				if (best == null)
				{
					//A cycle with no cuttable edge (only inline oneof unions) is malformed and cannot be
					//broken by wrapping; bail out rather than spin forever.
					Console.Error.WriteLine($"[turbolink] WARNING: uncuttable message cycle in {serviceFile.FileName}; struct ordering may be incomplete.");
					break;
				}

				best.Field.NeedNativeMake = true;
				best.Owner.HasNativeMake = true;

				if (++guard > maxIterations)
				{
					Console.Error.WriteLine($"[turbolink] WARNING: cycle-breaking exceeded iteration bound in {serviceFile.FileName}.");
					break;
				}
			}
		}

		// DFS for a single directed cycle over non-cut message edges. Returns the edges forming the
		// cycle in path order (entry node -> ... -> back-edge), or null when the graph is acyclic.
		private List<MessageEdge> FindOneCycle(GrpcServiceFile serviceFile)
		{
			int n = serviceFile.MessageArray.Count;
			int[] state = new int[n]; //0=white, 1=gray(on stack), 2=black
			var nodeStack = new List<int>();
			var edgeStack = new List<MessageEdge>();
			List<MessageEdge> result = null;

			bool Visit(int u)
			{
				state[u] = 1;
				nodeStack.Add(u);

				var owner = serviceFile.MessageArray[u];
				if (owner.Fields != null)
				{
					foreach (var field in owner.Fields)
					{
						if (field.NeedNativeMake) continue; //already cut
						if (!TryGetFieldTarget(serviceFile, field, out int v, out bool cuttable)) continue;
						if (v < 0 || v >= n) continue;

						var edge = new MessageEdge { Owner = owner, Field = field, Cuttable = cuttable };
						if (state[v] == 1)
						{
							//back edge closing a cycle: slice the path from where v entered the stack
							int k = nodeStack.IndexOf(v);
							result = new List<MessageEdge>();
							for (int i = k; i < edgeStack.Count; i++) result.Add(edgeStack[i]);
							result.Add(edge);
							return true;
						}
						if (state[v] == 0)
						{
							edgeStack.Add(edge);
							if (Visit(v)) return true;
							edgeStack.RemoveAt(edgeStack.Count - 1);
						}
					}
				}

				state[u] = 2;
				nodeStack.RemoveAt(nodeStack.Count - 1);
				return false;
			}

			for (int i = 0; i < n; i++)
			{
				if (state[i] == 0 && Visit(i)) return result;
			}
			return null;
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
					//NeedNativeMake fields are TSharedPtr (forward-decl only): no ordering constraint,
					//and honoring them would reintroduce the cycle that BreakMessageCycles just cut.
					if (messageField.NeedNativeMake) continue;

					if (!TryGetFieldTarget(serviceFile, messageField, out int index, out _)) continue;

					if(index >= message.Index)
					{
						// move the dependency in front of its user, then re-sort
						var msg = serviceFile.MessageArray[index];
						serviceFile.MessageArray.RemoveAt(index);
						serviceFile.MessageArray.Insert(message.Index, msg);

						// rebuild message index map
						RebuildMessageIndices(serviceFile);
						return false;
					}
				}
			}

			return true;
		}

		private void ParseComments(string protoFileName)
		{
			var serviceFile = GrpcServiceFiles[protoFileName];
			serviceFile.CommentParser = new ProtoCommentParser(serviceFile.ProtoFileDesc);
			serviceFile.CommentParser.ParseAllMessages();

			VerifyDefaultValues(serviceFile);
		}

		private void VerifyDefaultValues(GrpcServiceFile serviceFile)
		{
			foreach (var message in serviceFile.MessageArray)
			{
				if(message.Fields == null) continue;
				
				foreach (var field in message.Fields)
				{
					if (field.FieldDesc != null && ProtoCommentParser.FindFieldMeta(field.FieldDesc, CommentTagDefine.DefaultValue, out var info))
					{
						field.FieldDefaultValue = " = " + info;
					}
				}
			}
		}
	}
}
