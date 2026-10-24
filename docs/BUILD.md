# Visual Studio에서 빌드하기

1. .NET 10을 지원하는 Visual Studio와 **.NET 데스크톱 개발** 워크로드를 설치합니다.
2. `MyToDoWidget.sln`을 열고 `MyToDoWidget`을 시작 프로젝트로 지정합니다.
3. 일반 빌드로 실행합니다. 생성되는 프로그램 이름은 **MyToDo2**입니다.
4. 자체 포함 배포는 앱 프로젝트 **우클릭 → 게시 → Standalone-win-x64** 프로필을 선택합니다.

프로필은 Release / win-x64 / 자체 포함 / 단일 실행 파일 / 트리밍 끄기입니다. 출력 폴더는 `dist/MyToDo2-2.0.1-win-x64/`, 실행 파일은 `MyToDo2.exe`입니다.

## ZIP과 SHA-256 한 번에 만들기

.NET 10 SDK를 설치한 Windows에서 소스 폴더의 **`build_release.bat`을 실행**합니다. PowerShell 터미널에서는 `./build_release.bat`을 입력합니다. `build_release.ps1`도 같은 폴더에 두세요. 관리자 권한이나 별도 압축 프로그램은 필요하지 않습니다.

성공하면 `dist/`에 다음 파일이 함께 생성됩니다.

- `MyToDo2-2.0.1-win-x64.zip`: 자체 포함 실행 파일, 사용 안내와 라이선스
- `MyToDo2-2.0.1-win-x64.zip.sha256`: 위 ZIP의 SHA-256 해시와 파일명

버전은 `Directory.Build.props`에서 읽습니다. 빌드할 때마다 새 임시 폴더를 사용하며, 소스·개인 개발 자료·사용자 데이터·PDB는 실행용 ZIP에 포함하지 않습니다. 게시가 실패하면 ZIP과 체크섬을 갱신하지 않고 오류를 표시합니다. 터미널·자동화에서 마지막 일시 정지를 생략하려면 `./build_release.bat --no-pause`를 사용합니다.

`Developer (1)`처럼 폴더 이름에 공백·괄호가 있어도 사용할 수 있습니다. 이전 스크립트에서 MSB1008이 발생했다면 수정된 `build_release.ps1`으로 교체한 뒤 같은 BAT을 다시 실행하세요.

## 설치 코드 내장

빌드할 때 Google 시트·템플릿 주소를 입력하지 않습니다. `apps-script/MyToDoSetup.gs`를 내장 리소스로 포함하며, 위젯의 ‘설치 코드 복사’가 이 전체 내용을 복사합니다. 제3자 빌드에도 같은 파일을 사용합니다. 빌드에 Python이나 별도 Google API 설정은 필요하지 않습니다.

최초 연결은 [4단계 안내](APPS_SCRIPT_SETUP_KO.md)를 따릅니다. 사용자 본인의 빈 시트에서 설치·승인·배포하므로 개발자의 원본 시트나 웹 리디렉션에 의존하지 않습니다. Google의 권한 승인과 웹 앱 배포는 사용자가 직접 진행해야 합니다.

`TemplateSource.json`, `TemplateSource.private.json`, `TemplateSource.example.json`은 복사하거나 내장하지 않습니다. Visual Studio 빌드·게시에서도 제외하고 현재 출력 폴더에 남은 같은 이름의 파일을 정리합니다. BAT도 새 게시 결과에서 제거한 뒤 ZIP과 SHA-256을 만듭니다. 과거 출력 폴더·ZIP은 소급 변경되지 않습니다.

## GitHub 릴리즈

저장소는 `lottoria-dev/MyToDoWidget`, 태그는 **2.0.1**입니다. 위에서 생성한 **ZIP과 `.sha256` 두 파일**을 같은 태그의 Releases에 첨부합니다. 이 소스 묶음에는 실행 파일을 포함하지 않습니다.

다운로드한 ZIP을 확인하려면 PowerShell에서 다음을 실행해 `True`인지 확인합니다.

```powershell
$zip = 'MyToDo2-2.0.1-win-x64.zip'
$expected = ((Get-Content -LiteralPath ($zip + '.sha256') -Raw).Trim() -split '\s+')[0]
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -eq $expected
```

게시 프로필의 런타임은 .NET / Windows Desktop 10.0.13입니다. 런타임을 바꾸면 함께 배포하는 라이선스와 고지도 해당 버전으로 갱신하세요.
