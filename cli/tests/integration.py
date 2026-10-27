"""Black-box CLI tests. Fixtures contain synthetic files only; no desktop input is sent."""
import argparse,hashlib,json,sqlite3,subprocess,tempfile
from pathlib import Path
from PIL import Image,ImageDraw
import pymupdf as pdf

parser=argparse.ArgumentParser()
parser.add_argument('--exe',required=True)
parser.add_argument('--pdf-exe',required=True)
parser.add_argument('--helper-fixture',required=True)
parser.add_argument('--report')
options=parser.parse_args();checks=[]
with tempfile.TemporaryDirectory(prefix='RuaCLI-test-') as tmp:
 root=Path(tmp);inside=root/'allowed';inside.mkdir();source=inside/'한글 원본.pdf'
 with pdf.open() as d:
  d.new_page(width=400,height=500).insert_text((40,70),'CLI fixture first page')
  d.new_page(width=400,height=500).insert_text((40,70),'CLI second page');d.save(source)
 image=inside/'도장.png';im=Image.new('RGBA',(240,120),'white');ImageDraw.Draw(im).ellipse((20,15,220,110),fill='red');im.save(image)
 outside=root/'outside.pdf';outside.write_bytes(source.read_bytes());db=inside/'index.db'
 with sqlite3.connect(db) as c:
  c.executescript('CREATE TABLE files(id INTEGER PRIMARY KEY,path TEXT,name TEXT,size INTEGER,modified INTEGER);CREATE TABLE documents(id INTEGER PRIMARY KEY,text TEXT);')
  c.execute('INSERT INTO files VALUES(1,?,?,1,1)',(str(source),'한글 원본.pdf'));c.execute('INSERT INTO files VALUES(2,?,?,1,2)',(str(outside),'한글 밖.pdf'));c.execute('INSERT INTO documents VALUES(1,?)',('검색 테스트',))
 config=root/'config.json';config.write_text(json.dumps({'allowed_roots':[str(inside)],'apps':{'pdf':options.pdf_exe,'helper':options.helper_fixture,'image':'missing.exe','find':'missing.exe'},'find_database':str(db)}),encoding='utf-8')
 c.close()
 before={p:hashlib.sha256(p.read_bytes()).hexdigest() for p in [source,image,db]}
 def run(*args,error=False):
  p=subprocess.run([options.exe,'--config',str(config),*map(str,args)],capture_output=True,text=True,encoding='utf-8',timeout=145)
  value=json.loads(p.stdout);assert value['ok']!=error,(args,value,p.stderr);assert (p.returncode!=0)==error,(args,p.returncode)
  checks.append(' '.join(map(str,args[:2]))+(' rejected' if error else ' passed'));return value
 assert run('--version')['version']=='1.0.0';assert len(run('tools')['tools'])==17
 assert run('status')['result']['find_index_available']
 assert run('pdf','inspect','--input',source)['result']['page_count']==2
 assert run('pdf','inspect','--input',source,'--pages','1')['result']['pages'][0]['text'].startswith('CLI')
 run('pdf','inspect','--input',outside,error=True)
 for op in ['extract','duplicate','delete_pages','rotate']:
  out=inside/(op+'.pdf');run('pdf','pages','--input',source,'--output',out,'--operation',op,'--pages','1')
  with pdf.open(out) as d:assert len(d)==(1 if op in ['extract','delete_pages'] else 3 if op=='duplicate' else 2)
 run('pdf','add-text','--input',source,'--output',inside/'text.pdf','--page','1','--rect','40,100,250,140','--text','한글 CLI')
 run('pdf','insert-image','--input',source,'--output',inside/'stamp.pdf','--page','1','--rect','40,150,200,230','--image',image)
 hits=run('find','search','--query','한글')['result']['hits'];assert len(hits)==1 and hits[0]['path']==str(source)
 assert len(run('find','search','--query','검색','--contents','true')['result']['hits'])==1
 assert run('image','info','--input',image)['result']['width']==240
 for op,flags,size in [('resize',['--width','100'],(100,50)),('rotate',['--degrees','90'],(120,240)),('flip',[],(240,120)),('crop',['--x','10','--y','10','--width','60','--height','40'],(60,40)),('grayscale',[],(240,120)),('remove-background',['--tolerance','20'],(240,120)),('convert',[],(240,120))]:
  out=inside/(op+'.png');run('image',op,'--input',image,'--output',out,*flags)
  with Image.open(out) as check:assert check.size==size
 run('image','flip','--input',image,'--output',image,error=True)
 run('image','flip','--input',image,'--output',root/'outside.png',error=True)
 for args in [('image','resize','--width','oops'),('image','info','--input',image,'--source',image),('helper','overlay','--visible','yes'),('helper','attach'),('call','unknown','--args','{}')]:run(*args,error=True)
 argsfile=root/'args.json';argsfile.write_text(json.dumps({'source':str(image)}));assert run('call','ruaimage_info','--args-file',argsfile)['result']['height']==120
 examples=[(('helper','windows'),[],'list_windows'),(('helper','profiles'),[],'list_profiles'),(('helper','attach'),['--process-name','Calculator'],'attach'),(('helper','macro','run'),['--name','test'],'run_macro'),(('helper','stop'),[],'stop'),(('helper','overlay'),['--visible','false','--index','0'],'overlay'),(('helper','status'),[],'status')]
 for command,flags,name in examples:assert run(*command,*flags)['result']['tool']==name
 macro=root/'macro.json';macro.write_text(json.dumps({'Name':'test','Steps':[],'Repeat':1,'Speed':1}));reply=run('helper','macro','save','--macro-file',macro)['result'];assert reply['tool']=='save_macro' and reply['arguments']['macro']['Name']=='test'
 init=root/'fresh.json';run('config','init','--output',init);run('config','init','--output',init,error=True)
 for path,digest in before.items():assert hashlib.sha256(path.read_bytes()).hexdigest()==digest
 checks.append('original PDF/image/index SHA-256 unchanged')
report={'ok':True,'version':'1.0.0','checks':len(checks),'passed':checks,'tools':17,'desktop_input_sent':False}
if options.report:Path(options.report).write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({'ok':True,'checks':len(checks),'tools':17}))
