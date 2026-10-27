using System.Text.Json;
string? line;
while((line=Console.ReadLine())!=null){using var doc=JsonDocument.Parse(line);var r=doc.RootElement;var id=r.GetProperty("id").Clone();var method=r.GetProperty("method").GetString();object result;
if(method=="initialize")result=new{protocolVersion="2024-11-05",capabilities=new{},serverInfo=new{name="fixture",version="1"}};
else{var p=r.GetProperty("params");result=new{content=new[]{new{type="text",text=JsonSerializer.Serialize(new{tool=p.GetProperty("name").GetString(),arguments=p.GetProperty("arguments").Clone()})}},isError=false};}
Console.WriteLine(JsonSerializer.Serialize(new{jsonrpc="2.0",id,result}));}
