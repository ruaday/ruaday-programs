using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace RuaImage.Core;

public static class Imaging
{
 public const long MaxPixels=40_000_000;
 public static void CheckSize(int w,int h) { if(w<1||h<1||w>24000||h>24000||(long)w*h>MaxPixels) throw new InvalidDataException("이미지는 최대 4천만 화소, 한 변 24,000px까지 처리할 수 있어요."); }
 public static BitmapSource Load(string path,int preview=0)
 {
  using var stream=File.OpenRead(path);
  var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
  var frame=decoder.Frames[0]; if(preview==0) CheckSize(frame.PixelWidth,frame.PixelHeight);
  stream.Position=0; var image=new BitmapImage(); image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream;
  if(preview>0) image.DecodePixelWidth=preview; image.EndInit();image.Freeze();
  // Normalize EXIF orientation once, without retaining a file handle.
  var orientation=1;try {if(frame.Metadata is BitmapMetadata m && m.ContainsQuery("/app1/ifd/{ushort=274}")) orientation=Convert.ToInt32(m.GetQuery("/app1/ifd/{ushort=274}"));}catch(NotSupportedException){}
  BitmapSource result=image;
  if(orientation is 2 or 4 or 5 or 7) result=Flip(result);
  if(orientation==3||orientation==4) result=Rotate(result,180);
  else if(orientation==6||orientation==7) result=Rotate(result,90);
  else if(orientation==5||orientation==8) result=Rotate(result,270);
  return Bgra(result);
 }
 public static BitmapSource Bgra(BitmapSource source) { if(source.Format==PixelFormats.Bgra32) return source; var b=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0); b.Freeze();return b; }
 public static byte[] Pixels(BitmapSource source) { var b=Bgra(source); var p=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(p,b.PixelWidth*4,0); return p; }
 public static BitmapSource From(byte[] p,int w,int h) {var b=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,p,w*4);b.Freeze();return b;}
 public static BitmapSource Rotate(BitmapSource s,int degrees=90) {var b=new TransformedBitmap(s,new RotateTransform(degrees));b.Freeze();return Bgra(b);}
 public static BitmapSource Flip(BitmapSource s) {var b=new TransformedBitmap(s,new ScaleTransform(-1,1));b.Freeze();return Bgra(b);}
 public static BitmapSource Crop(BitmapSource s,Int32Rect r) {var b=new CroppedBitmap(s,r);b.Freeze();return Bgra(b);}
 public static BitmapSource Resize(BitmapSource s,int w,int h)
 {
  CheckSize(w,h); return Render(w,h,dc=>dc.DrawImage(s,new Rect(0,0,w,h)));
 }
 public static BitmapSource Render(int w,int h,Action<DrawingContext> draw)
 {
  CheckSize(w,h);var v=new DrawingVisual();RenderOptions.SetBitmapScalingMode(v,BitmapScalingMode.HighQuality);using(var dc=v.RenderOpen()) draw(dc);
  var b=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);b.Render(v);b.Freeze();return Bgra(b);
 }
 public static BitmapSource Overlay(BitmapSource s,BitmapSource? overlay,string text,int x,int y,int width,double opacity,int fontSize)
 {
  return Render(s.PixelWidth,s.PixelHeight,dc=> {dc.DrawImage(s,new Rect(0,0,s.PixelWidth,s.PixelHeight));dc.PushOpacity(opacity);
   if(overlay!=null) dc.DrawImage(overlay,new Rect(x,y,width,Math.Max(1,(double)width*overlay.PixelHeight/overlay.PixelWidth)));
   if(!string.IsNullOrEmpty(text)) {var ft=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Malgun Gothic"),fontSize,Brushes.White,1);dc.DrawText(ft,new Point(x+1,y+1));var ink=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Malgun Gothic"),fontSize,new SolidColorBrush(Color.FromRgb(94,65,118)),1);dc.DrawText(ink,new Point(x,y));}dc.Pop();});
 }
 public static BitmapSource Clear(BitmapSource s,Int32Rect r)
 {var p=Pixels(s);for(var y=r.Y;y<r.Y+r.Height;y++)for(var x=r.X;x<r.X+r.Width;x++)p[(y*s.PixelWidth+x)*4+3]=0;return From(p,s.PixelWidth,s.PixelHeight);}
 public static BitmapSource Brush(BitmapSource s,BitmapSource original,IEnumerable<Point> points,double radius,bool restore)
 {
  var p=Pixels(s); var o=restore?Pixels(original):null;int w=s.PixelWidth,h=s.PixelHeight;
  if(restore&&(original.PixelWidth!=w||original.PixelHeight!=h)) throw new InvalidOperationException("크기 변경 전 복원 기준은 사용할 수 없어요.");
  foreach(var point in points) for(var y=Math.Max(0,(int)(point.Y-radius));y<Math.Min(h,(int)(point.Y+radius+1));y++) for(var x=Math.Max(0,(int)(point.X-radius));x<Math.Min(w,(int)(point.X+radius+1));x++)
  {var distance=Math.Sqrt(Math.Pow(x-point.X,2)+Math.Pow(y-point.Y,2));if(distance>radius)continue;var i=(y*w+x)*4;var strength=Math.Clamp(radius-distance,0,1);if(restore){for(int c=0;c<4;c++)p[i+c]=(byte)(p[i+c]*(1-strength)+o![i+c]*strength);}else p[i+3]=(byte)(p[i+3]*(1-strength));}
  return From(p,w,h);
 }
 public static BitmapSource RemoveBorderBackground(BitmapSource s,int tolerance,CancellationToken token=default)
 {
  var p=Pixels(s);int w=s.PixelWidth,h=s.PixelHeight;var visited=new bool[w*h];var queue=new Queue<int>();
  // Each connected border segment uses its own sampled colour; foreground holes stay intact.
  int[] corners={0,(w-1)*4,(w*(h-1))*4,(w*h-1)*4}; var colors=corners.Select(i=>new[]{(int)p[i],p[i+1],p[i+2]}).ToArray();
  bool Match(int k) {int i=k*4;return colors.Any(c=>Math.Max(Math.Abs(p[i]-c[0]),Math.Max(Math.Abs(p[i+1]-c[1]),Math.Abs(p[i+2]-c[2])))<=tolerance);}
  void Add(int k){if(!visited[k]){visited[k]=true;if(Match(k))queue.Enqueue(k);}}
  for(int x=0;x<w;x++){Add(x);Add((h-1)*w+x);}for(int y=0;y<h;y++){Add(y*w);Add(y*w+w-1);}
  int n=0;while(queue.TryDequeue(out var k)){if((++n&8191)==0)token.ThrowIfCancellationRequested();p[k*4+3]=0;int x=k%w,y=k/w;if(x>0)Add(k-1);if(x<w-1)Add(k+1);if(y>0)Add(k-w);if(y<h-1)Add(k+w);}
  return From(p,w,h);
 }
 public static BitmapSource Filter(BitmapSource s,int mode,CancellationToken token=default)
 {
  var p=Pixels(s);var original=(mode>=5)?(byte[])p.Clone():null;int w=s.PixelWidth,h=s.PixelHeight;
  for(int y=0;y<h;y++){if((y&31)==0)token.ThrowIfCancellationRequested();for(int x=0;x<w;x++){int i=(y*w+x)*4;double b=p[i],g=p[i+1],r=p[i+2];
   if(mode==0){b=g=r=.299*r+.587*g+.114*b;}else if(mode==1){var nr=.393*r+.769*g+.189*b;var ng=.349*r+.686*g+.168*b;b=.272*r+.534*g+.131*b;r=nr;g=ng;}
   else if(mode==2){b=255-b;g=255-g;r=255-r;}else if(mode==3){b+=22;g+=22;r+=22;}else if(mode==4){b-=22;g-=22;r-=22;}
   if(mode>=5&&x>0&&x<w-1&&y>0&&y<h-1){for(int c=0;c<3;c++){double sum=0;if(mode==5)sum=original![i+c]*5-original[i-4+c]-original[i+4+c]-original[i-w*4+c]-original[i+w*4+c];else{for(int yy=-1;yy<=1;yy++)for(int xx=-1;xx<=1;xx++)sum+=original![( (y+yy)*w+x+xx)*4+c]/9d;}p[i+c]=(byte)Math.Clamp(sum,0,255);}continue;}
   p[i]=(byte)Math.Clamp(b,0,255);p[i+1]=(byte)Math.Clamp(g,0,255);p[i+2]=(byte)Math.Clamp(r,0,255);
  }}return From(p,w,h);
 }
 public static void Save(BitmapSource s,string path)
 {
  var ext=Path.GetExtension(path).ToLowerInvariant();BitmapEncoder encoder=ext switch{".jpg" or ".jpeg"=>new JpegBitmapEncoder{QualityLevel=95},".bmp"=>new BmpBitmapEncoder(),".tif" or ".tiff"=>new TiffBitmapEncoder(),".png"=>new PngBitmapEncoder(),_=>throw new NotSupportedException("PNG, JPG, BMP, TIFF로 저장할 수 있어요.")};
  if(encoder is JpegBitmapEncoder) s=Render(s.PixelWidth,s.PixelHeight,dc=>{dc.DrawRectangle(Brushes.White,null,new Rect(0,0,s.PixelWidth,s.PixelHeight));dc.DrawImage(s,new Rect(0,0,s.PixelWidth,s.PixelHeight));});
  encoder.Frames.Add(BitmapFrame.Create(s));WriteAtomic(path,stream=>encoder.Save(stream));
 }
 public static void WriteAtomic(string path,Action<Stream> write,bool overwrite=false)
 {
  var full=Path.GetFullPath(path);var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write)){write(stream);stream.Flush(true);}File.Move(temp,full,overwrite);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public static void Gif(IReadOnlyList<string> paths,string output,int delay,int width,CancellationToken token)
 {
  if(paths.Count<2)throw new InvalidOperationException("GIF에는 이미지가 2장 이상 필요해요.");
  if(paths.Count>300)throw new InvalidOperationException("한 GIF는 최대 300장까지 만들 수 있어요.");
  var first=Load(paths[0]);int height=Math.Max(1,first.PixelHeight*width/first.PixelWidth);if((long)width*height*paths.Count>100_000_000)throw new InvalidOperationException("GIF 크기나 프레임 수를 줄여주세요 (총 1억 화소 제한).");
  var encoder=new GifBitmapEncoder();foreach(var path in paths){token.ThrowIfCancellationRequested();var s=Load(path);double scale=Math.Min((double)width/s.PixelWidth,(double)height/s.PixelHeight);var b=Render(width,height,dc=>{dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width,height));var w=s.PixelWidth*scale;var h=s.PixelHeight*scale;dc.DrawImage(s,new Rect((width-w)/2,(height-h)/2,w,h));});var meta=new BitmapMetadata("gif");meta.SetQuery("/grctlext/Delay",(ushort)Math.Clamp(delay/10,1,65535));meta.SetQuery("/grctlext/Disposal",(byte)2);
   if(encoder.Frames.Count==0){meta.SetQuery("/appext/application",System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));meta.SetQuery("/appext/data",new byte[]{3,1,0,0,0});}encoder.Frames.Add(BitmapFrame.Create(b,null,meta,null));}
  using var encoded=new MemoryStream();encoder.Save(encoded);var bytes=GifTiming(encoded.ToArray(),delay);
  WriteAtomic(output,stream=>stream.Write(bytes));
 }
 static byte[] GifTiming(byte[] data,int delay)
 {
  // WIC's encoder drops frame metadata on some Windows builds. Write GIF89a controls explicitly.
  using var output=new MemoryStream();using var writer=new BinaryWriter(output);int i=13;var packed=data[10];if((packed&128)!=0)i+=3*(1<<((packed&7)+1));writer.Write(data,0,i);
  writer.Write(new byte[]{0x21,0xFF,11});writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));writer.Write(new byte[]{3,1,0,0,0});byte flags=0,index=0;
  int Blocks(int position){while(data[position]!=0)position+=1+data[position];return position+1;}
  while(i<data.Length){int start=i;byte type=data[i++];
   if(type==0x3B){writer.Write(type);break;}
   if(type==0x21){byte label=data[i++];if(label==0xF9){flags=data[i+1];index=data[i+4];i+=6;continue;}int end=Blocks(i);bool loop=label==0xFF&&data[i]==11&&System.Text.Encoding.ASCII.GetString(data,i+1,11)=="NETSCAPE2.0";if(!loop)writer.Write(data,start,end-start);i=end;continue;}
   if(type==0x2C){writer.Write(new byte[]{0x21,0xF9,4,(byte)(8|(flags&1))});writer.Write((ushort)Math.Clamp(delay/10,1,65535));writer.Write(index);writer.Write((byte)0);byte local=data[i+8];i+=9;if((local&128)!=0)i+=3*(1<<((local&7)+1));i++;int end=Blocks(i);writer.Write(data,start,end-start);i=end;flags=index=0;continue;}
   throw new InvalidDataException("GIF 인코딩 구조를 확인할 수 없어요.");
  }return output.ToArray();
 }
}
