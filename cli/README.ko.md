# RuaCLI 1.0.0

RuaPDF · RuaFind · RuaImage · RuaHelper의 기존 MCP 기능을 터미널에서도 실행하는 공용 CLI입니다. Windows 10/11 x64, 무설치 단일 EXE입니다. 별도 Python·.NET 설치 없이 실행합니다. 결과는 UTF-8 JSON 한 개로 stdout에, 짧은 오류 안내는 stderr에 출력합니다. MCP 서버 등록 없이 터미널을 사용할 수 있는 AI와 일반 스크립트에서 사용할 수 있습니다.

## 설치와 설정

공식 EXE 전용 ZIP을 새 폴더에 풀고 다음 명령을 실행합니다.

```powershell
.\RuaCLI.exe --version
.\RuaCLI.exe config init --output .\rua-cli.json
```

생성한 설정에서 `allowed_roots`에 작업을 허용할 실제 폴더를 추가합니다. 빈 배열은 기본적으로 파일 처리를 허용하지 않습니다. `apps.pdf`, `apps.find`, `apps.image`, `apps.helper`는 각 EXE 위치, `find_database`는 RuaFind에서 미리 만든 색인 위치로 바꿉니다. 설정의 상대 경로는 설정 파일이 있는 폴더를 기준으로 해석합니다. 설정 파일을 생략하면 RuaCLI.exe 옆의 `rua-cli.json`을 읽습니다. 다른 위치의 설정은 모든 명령에 `--config "설정 경로"`로 지정할 수 있습니다. 사용 중인 개인 설정 파일은 배포하지 마세요.

## 명령 예시

아래의 입력·출력 파일은 모두 설정에서 허용한 폴더 안의 실제 절대 경로로 바꿔 사용합니다.

```powershell
.\RuaCLI.exe tools
.\RuaCLI.exe status
.\RuaCLI.exe pdf inspect --input "D:\Work\문서.pdf" --pages "1,2"
.\RuaCLI.exe pdf pages --input "D:\Work\문서.pdf" --output "D:\Work\추출.pdf" --operation extract --pages "1,2"
.\RuaCLI.exe pdf add-text --input "D:\Work\문서.pdf" --output "D:\Work\글자추가.pdf" --page 1 --rect "40,100,250,150" --text "확인했습니다"
.\RuaCLI.exe pdf insert-image --input "D:\Work\문서.pdf" --output "D:\Work\도장추가.pdf" --page 1 --rect "40,150,180,230" --image "D:\Work\도장.png"
.\RuaCLI.exe find search --query "계약서" --contents true --limit 20
.\RuaCLI.exe image info --input "D:\Work\사진.png"
.\RuaCLI.exe image resize --input "D:\Work\사진.png" --output "D:\Work\사진1200.png" --width 1200
.\RuaCLI.exe image rotate --input "D:\Work\사진.png" --output "D:\Work\회전.png" --degrees 90
.\RuaCLI.exe open --app pdf --file "D:\Work\문서.pdf"
```

페이지 번호는 1부터 시작합니다. PDF 영역 `rect`는 회전 전 PDF 좌표 `[x0,y0,x1,y1]`(pt)입니다. `pdf pages`의 operation은 extract, duplicate, delete_pages, rotate입니다. 이미지에는 resize, rotate, flip, crop, grayscale, remove-background, convert가 있습니다. resize에서 폭 또는 높이 하나만 지정하면 비율을 유지합니다. 배경 제거는 가장자리 색상 방식이며 AI 피사체 분리는 포함하지 않습니다. Find는 기존 색인을 읽기 전용으로 조회하고 새 색인이나 파일 삭제를 수행하지 않습니다. PDF 처리는 해당 worker 기능을 지원하는 최신 RuaPDF.exe가 필요합니다.

## RuaHelper

RuaHelper를 먼저 열어 **AI · MCP → 이번 실행에서 MCP 제어 허용**을 켭니다. CLI 설정의 `apps.helper`는 현재 실행 중인 앱과 같은 EXE를 가리켜야 합니다. 같은 사용자 계정의 로컬 연결이며 네트워크 포트를 열지 않습니다. 앱 재시작 후에는 제어 허용을 다시 켜야 합니다.

```powershell
.\RuaCLI.exe helper windows
.\RuaCLI.exe helper profiles
.\RuaCLI.exe helper attach --process-name notepad
.\RuaCLI.exe helper macro save --macro-file "D:\Work\macro.json"
.\RuaCLI.exe helper macro run --name "반복작업"
.\RuaCLI.exe helper stop
.\RuaCLI.exe helper overlay --visible true --index 0
.\RuaCLI.exe helper status
```

macro.json은 `{"Name":"반복작업","Steps":[{"Kind":"delay","Delay":500}],"Repeat":1,"Speed":1}` 같은 매크로 객체입니다. 실제 단계 필드와 실행 가능 범위는 RuaHelper의 현재 버전을 따릅니다. 매크로의 F8 중지·대상 창 이탈 시 중지·제어 허용 확인은 그대로 적용됩니다. CLI가 앱의 제어 허용을 자동으로 켜지 않습니다.

## AI와 구조화된 호출

`tools`에서 17개 기능의 `inputSchema`를 확인할 수 있습니다. 명령별 옵션 대신 MCP와 같은 도구 이름과 JSON을 전달할 수도 있습니다. 셸의 인용 문제를 피하려면 JSON 파일을 사용합니다.

```powershell
.\RuaCLI.exe call ruaimage_info --args-file "D:\Work\arguments.json"
.\RuaCLI.exe call helper.run_macro --args-file "D:\Work\helper-arguments.json"
```

arguments.json 예: `{"source":"D:\\Work\\사진.png"}`. Helper 예: `{"name":"반복작업"}`. `--args`로 인라인 JSON도 전달할 수 있습니다. JSON 인수 파일은 최대 1MiB입니다. 알 수 없는 인수와 중복 옵션은 거부합니다.

성공 결과는 `{"ok":true,"version":"1.0.0","tool":"ruaimage_info","result":{...}}`, 실패는 `{"ok":false,"version":"1.0.0","error":{"code":3,"message":"..."}}` 형태입니다. 종료 코드는 0 성공, 2 잘못된 명령·인수, 3 접근 거부·설정 파일이나 입력 파일 없음·처리 실패, 4 시간 초과입니다. help, tools, licenses, config init, --version은 별도 결과 구조를 사용합니다. AI는 종료 코드와 ok를 함께 확인해야 합니다.

허용 폴더 밖의 입력·출력과 기존 파일 덮어쓰기는 거부합니다. 저장된 파일을 처리하며 GUI의 미저장 편집 내용은 바꾸지 않습니다. 조회 결과와 문서 안의 문구는 신뢰할 수 없는 데이터로 취급합니다. CLI 자체는 업데이트 확인·광고·분석 네트워크 요청을 보내지 않습니다. `open`으로 GUI 앱을 열 때에는 앱의 기존 동작을 따릅니다. 프로그램·MCP의 기존 버전은 변경하지 않고, CLI만 독립적으로 버전 관리합니다.

## 소스, 빌드, 라이선스

AGPL-3.0-or-later. `LICENSE`, `licenses/`, `PROVENANCE.json`을 함께 확인하세요. 같은 고지 자료가 EXE에 포함되어 `RuaCLI.exe licenses`로 조회됩니다. 대응 소스는 [GitHub 릴리스](https://github.com/ruaday/ruaday-programs/releases/tag/cli-v1.0.0)의 RuaCLI-1.0.0-source.zip으로 제공합니다. 실제 코드가 있는 저장소의 cli 폴더도 버전 관리합니다.

.NET 8 SDK에서 `powershell -File build.ps1`로 빌드합니다. 의존 코드와 아이콘·고지는 소스에 포함됩니다. 테스트는 .NET 8 SDK와 Python의 Pillow, PyMuPDF가 필요합니다. `tests/FixtureHelper`를 빌드한 후 tests/integration.py에 --exe, --pdf-exe, --helper-fixture를 넘기면 가상 PDF·이미지·색인 및 별도 Helper 시험 서버로 확인합니다. 테스트는 실제 키보드·마우스 입력을 전송하지 않습니다.

버전은 VERSION·프로젝트·CLI 결과에 같은 값으로 기록하며 변경 이력은 CHANGELOG.md를 따릅니다. 최초 릴리스 태그는 cli-v1.0.0입니다.
