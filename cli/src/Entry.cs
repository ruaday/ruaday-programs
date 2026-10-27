using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuaDay.Cli;

static class Entry
{
    const string Version="1.0.0";
    static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
    static string configPath=Path.Combine(AppContext.BaseDirectory,"rua-cli.json");
    static readonly Dictionary<string,string> Aliases=new()
    {
        ["status"]="rua_status",["open"]="rua_open",
        ["pdf inspect"]="ruapdf_inspect",["pdf add-text"]="ruapdf_add_text",["pdf insert-image"]="ruapdf_insert_image",["pdf pages"]="ruapdf_pages",
        ["find search"]="ruafind_search",["image info"]="ruaimage_info",
        ["helper windows"]="helper.list_windows",["helper profiles"]="helper.list_profiles",["helper attach"]="helper.attach",
        ["helper macro save"]="helper.save_macro",["helper macro run"]="helper.run_macro",["helper stop"]="helper.stop",["helper overlay"]="helper.overlay",["helper status"]="helper.status"
    };
    static JsonElement[] Tools()=>RuaDay.Mcp.Program.Definitions().EnumerateArray().Concat(HelperTools().EnumerateArray()).Select(t=>t.Clone()).ToArray();
    static JsonElement HelperTools(){using var stream=typeof(Entry).Assembly.GetManifestResourceStream("helper-tools.json")!;return JsonDocument.Parse(stream).RootElement.Clone();}
    static void Print(object value)=>Console.WriteLine(JsonSerializer.Serialize(value,Json));
    [STAThread]
    static int Main(string[] raw)
    {
        Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
        System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
        try
        {
            var args=raw.ToList();
            var index=args.IndexOf("--config");
            if(index>=0){if(index+1>=args.Count)throw new ArgumentException("--config needs a path");configPath=Path.GetFullPath(args[index+1]);args.RemoveRange(index,2);if(args.Contains("--config"))throw new ArgumentException("Duplicate --config");}
            if(args.Count==0||args.SequenceEqual(new[]{"--help"})||args.SequenceEqual(new[]{"help"}))
            {
                Print(new{ok=true,version=Version,license="AGPL-3.0-or-later",source="https://github.com/ruaday/ruaday-programs/releases/tag/cli-v1.0.0",usage="RuaCLI.exe [--config rua-cli.json] <command> [--option value]",commands=Aliases.Keys.Concat(new[]{"image resize|rotate|flip|crop|grayscale|remove-background|convert","call <tool> --args <JSON> | --args-file <JSON file>","tools","licenses","config init --output <new JSON file>","--version"}),output="One UTF-8 JSON object. Exit codes: 0 success, 2 invalid arguments/configuration, 3 operation denied/failed, 4 timeout."});return 0;
            }
            if(args.SequenceEqual(new[]{"--version"})){Print(new{ok=true,name="RuaCLI",version=Version});return 0;}
            if(args.SequenceEqual(new[]{"tools"})){Print(new{ok=true,version=Version,tools=Tools()});return 0;}
            if(args.SequenceEqual(new[]{"licenses"}))
            {
                var assembly=typeof(Entry).Assembly;var notices=assembly.GetManifestResourceNames().Where(n=>n.StartsWith("legal.",StringComparison.Ordinal)).ToDictionary(n=>n[6..],n=>{using var stream=assembly.GetManifestResourceStream(n)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();});
                Print(new{ok=true,version=Version,source="https://github.com/ruaday/ruaday-programs/releases/tag/cli-v1.0.0",notices});return 0;
            }
            if(args.Count>=2&&args[0]=="config"&&args[1]=="init")
            {
                var flags=Flags(args.Skip(2));if(flags.Count!=1||!flags.TryGetValue("output",out var path))throw new ArgumentException("Use config init --output <new file>");
                var example=new{allowed_roots=Array.Empty<string>(),apps=new{pdf="../RuaPDF.exe",find="../RuaFind.exe",image="../RuaImage.exe",helper="../RuaHelper.exe"},find_database="../RuaFind.Data/index.db"};
                using var file=new FileStream(Path.GetFullPath(path),FileMode.CreateNew,FileAccess.Write,FileShare.None);JsonSerializer.Serialize(file,example,new JsonSerializerOptions{WriteIndented=true});
                Print(new{ok=true,version=Version,result=new{created=Path.GetFullPath(path),next="Edit executable paths, Find index and allowed_roots before running file commands."}});return 0;
            }
            string tool;var positional=args.TakeWhile(a=>!a.StartsWith("--",StringComparison.Ordinal)).ToArray();var command=string.Join(' ',positional);
            var values=Flags(args.Skip(positional.Length));var supplied=new JsonObject();
            if(positional.Length==2&&positional[0]=="call")
            {
                tool=positional[1];
                if(values.Count!=1||!(values.ContainsKey("args")||values.ContainsKey("args-file")))throw new ArgumentException("call needs exactly one of --args or --args-file");
                supplied=JsonNode.Parse(values.TryGetValue("args",out var inline)?inline:ReadJsonFile(values["args-file"])) as JsonObject??throw new ArgumentException("Arguments must be a JSON object");
            }
            else
            {
                string? operation=null;
                if(positional.Length==2&&positional[0]=="image"&&new[]{"resize","rotate","flip","crop","grayscale","remove-background","convert"}.Contains(positional[1])){tool="ruaimage_transform";operation=positional[1].Replace('-','_');}
                else if(!Aliases.TryGetValue(command,out tool!))throw new ArgumentException("Unknown command. Use --help or tools.");
                var definition=Tools().First(t=>t.GetProperty("name").GetString()==tool);var properties=definition.GetProperty("inputSchema").GetProperty("properties");
                foreach(var (key,value) in values)
                {
                    var field=key switch{"input"=>"source","process-name"=>"processName",_=>key.Replace('-','_')};
                    if(key=="macro-file"&&tool=="helper.save_macro"){supplied["macro"]=JsonNode.Parse(ReadJsonFile(value)) as JsonObject??throw new ArgumentException("Macro must be an object");continue;}
                    if(!properties.TryGetProperty(field,out var schema))throw new ArgumentException("Unknown option: --"+key);
                    if(supplied.ContainsKey(field))throw new ArgumentException("Duplicate argument: "+field);
                    supplied[field]=Value(value,schema.GetProperty("type").GetString());
                }
                if(operation!=null){if(supplied.ContainsKey("operation"))throw new ArgumentException("Do not repeat --operation with an image subcommand");supplied["operation"]=operation;}
            }
            var found=Tools().FirstOrDefault(t=>t.GetProperty("name").GetString()==tool);if(found.ValueKind==JsonValueKind.Undefined)throw new ArgumentException("Unknown tool: "+tool);
            JsonElement result;
            if(tool.StartsWith("helper.",StringComparison.Ordinal))
            {
                var a=JsonSerializer.SerializeToElement(supplied);Validate(a,found.GetProperty("inputSchema"));result=Helper(tool[7..],a);
            }
            else
            {
                if(tool=="ruaimage_transform"&&supplied["operation"]?.GetValue<string>()=="resize"&&supplied.ContainsKey("width")!=supplied.ContainsKey("height"))
                {
                    var info=RuaDay.Mcp.Program.Invoke(configPath,"ruaimage_info",JsonSerializer.SerializeToElement(new{source=supplied["source"]?.GetValue<string>()}));
                    var hasWidth=supplied.ContainsKey("width");var original=info.GetProperty(hasWidth?"width":"height").GetInt32();var other=info.GetProperty(hasWidth?"height":"width").GetInt32();
                    var requested=supplied[hasWidth?"width":"height"]!.GetValue<int>();if(requested<1||requested>20000)throw new ArgumentException("Resize dimensions must be 1..20000");
                    supplied[hasWidth?"height":"width"]=Math.Max(1,checked((int)Math.Round((double)requested*other/original)));
                }
                result=RuaDay.Mcp.Program.Invoke(configPath,tool,JsonSerializer.SerializeToElement(supplied));
            }
            Print(new{ok=true,version=Version,tool,result});return 0;
        }
        catch(Exception e)
        {
            var code=e is TimeoutException?4:e is ArgumentException or JsonException or FormatException or OverflowException?2:3;
            Console.Error.WriteLine("RuaCLI: "+e.Message);Print(new{ok=false,version=Version,error=new{code,message=e.Message}});return code;
        }
    }
    static Dictionary<string,string> Flags(IEnumerable<string> args)
    {
        var input=args.ToArray();var flags=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=0;i<input.Length;i++)
        {
            var token=input[i];if(!token.StartsWith("--")||token.Length==2)throw new ArgumentException("Expected --option value: "+token);
            var equal=token.IndexOf('=');var key=equal>=0?token[2..equal]:token[2..];
            var value=equal>=0?token[(equal+1)..]:++i<input.Length?input[i]:throw new ArgumentException("Missing value: "+token);
            if(!flags.TryAdd(key,value))throw new ArgumentException("Duplicate option: --"+key);
        }
        return flags;
    }
    static JsonNode? Value(string value,string? type)=>type switch
    {
        "string"=>JsonValue.Create(value),"integer"=>JsonValue.Create(int.Parse(value,CultureInfo.InvariantCulture)),
        "number"=>JsonValue.Create(double.Parse(value,CultureInfo.InvariantCulture)),"boolean"=>JsonValue.Create(value=="true"?true:value=="false"?false:throw new ArgumentException("Boolean options use true or false")),
        "array"=>JsonNode.Parse(value.StartsWith('[')?value:"["+value+"]") as JsonArray??throw new ArgumentException("Array required"),
        "object"=>JsonNode.Parse(value) as JsonObject??throw new ArgumentException("Object required"),_=>throw new ArgumentException("Unsupported argument type")
    };
    static string ReadJsonFile(string path)
    {
        var file=new FileInfo(path);if(file.Length>1_048_576)throw new ArgumentException("JSON argument file exceeds 1MiB");return File.ReadAllText(path,Encoding.UTF8);
    }
    static void Validate(JsonElement value,JsonElement schema)
    {
        var type=schema.GetProperty("type").GetString();
        var valid=type switch{"object"=>value.ValueKind==JsonValueKind.Object,"array"=>value.ValueKind==JsonValueKind.Array,"string"=>value.ValueKind==JsonValueKind.String,"integer"=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out _),"number"=>value.ValueKind==JsonValueKind.Number&&double.IsFinite(value.GetDouble()),"boolean"=>value.ValueKind is JsonValueKind.True or JsonValueKind.False,_=>false};
        if(!valid)throw new ArgumentException("Invalid argument type; expected "+type);
        if(schema.TryGetProperty("minimum",out var min)&&value.GetDouble()<min.GetDouble()||schema.TryGetProperty("maximum",out var max)&&value.GetDouble()>max.GetDouble())throw new ArgumentException("Argument out of range");
        if(type=="object")
        {
            if(schema.TryGetProperty("required",out var required))foreach(var item in required.EnumerateArray())if(!value.TryGetProperty(item.GetString()!,out _))throw new ArgumentException("Missing argument: "+item.GetString());
            if(schema.TryGetProperty("properties",out var properties))foreach(var prop in value.EnumerateObject())
            {
                if(properties.TryGetProperty(prop.Name,out var child))Validate(prop.Value,child);
                else if(schema.TryGetProperty("additionalProperties",out var extra)&&extra.ValueKind==JsonValueKind.False)throw new ArgumentException("Unknown argument: "+prop.Name);
            }
        }
    }
    static JsonElement Helper(string name,JsonElement arguments)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(configPath));var value=doc.RootElement.GetProperty("apps").GetProperty("helper").GetString()??"";
        var exe=Path.GetFullPath(Environment.ExpandEnvironmentVariables(value),Path.GetDirectoryName(configPath)!);
        if(!File.Exists(exe)||!exe.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Set apps.helper to the same RuaHelper.exe used by the running app.");
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardInputEncoding=new UTF8Encoding(false)};start.ArgumentList.Add("--mcp");
        using var process=Process.Start(start)??throw new IOException("Cannot start RuaHelper MCP");var errors=process.StandardError.ReadToEndAsync();
        try
        {
            JsonElement Rpc(int id,string method,object parameters)
            {
                process.StandardInput.WriteLine(JsonSerializer.Serialize(new{jsonrpc="2.0",id,method,@params=parameters}));process.StandardInput.Flush();
                var line=process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult()??throw new IOException("RuaHelper closed without a response");
                if(line.Length>16_777_216)throw new IOException("RuaHelper response exceeds 16MiB");using var response=JsonDocument.Parse(line);
                if(response.RootElement.GetProperty("id").GetInt32()!=id||response.RootElement.TryGetProperty("error",out _))throw new IOException("Invalid RuaHelper MCP response");return response.RootElement.GetProperty("result").Clone();
            }
            Rpc(1,"initialize",new{protocolVersion="2024-11-05",capabilities=new{},clientInfo=new{name="RuaCLI",version=Version}});
            var reply=Rpc(2,"tools/call",new{name,arguments});
            var text=reply.GetProperty("content")[0].GetProperty("text").GetString()??"";
            if(reply.TryGetProperty("isError",out var error)&&error.GetBoolean())throw new IOException(text);
            using var parsed=JsonDocument.Parse(text);return parsed.RootElement.Clone();
        }
        finally
        {
            try{process.StandardInput.Close();if(!process.WaitForExit(2000)){process.Kill(true);process.WaitForExit();}}catch{try{process.Kill(true);}catch{}}
        }
    }
}
