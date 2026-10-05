# 외부 구성요소 고지

MyToDo의 자체 소스 라이선스는 [LICENSE.md](LICENSE.md)의 MIT 라이선스입니다.
Windows x64 standalone 배포본에는 Microsoft .NET 10 런타임과 Windows Desktop 구성요소가 포함됩니다.

| 구성요소 | 용도 | 함께 보존할 고지 |
|---|---|---|
| Microsoft .NET Runtime | C# 프로그램 실행 | `licenses/DOTNET-RUNTIME-LICENSE.txt` |
| Windows Desktop: WPF / Windows Forms | 위젯·환경설정·트레이 | `licenses/DOTNET-WINDOWSDESKTOP-LICENSE.txt` |
| .NET에 포함되는 제3자 구성요소 | 런타임 의존 구성요소 | `licenses/DOTNET-THIRD-PARTY-NOTICES.txt` |

소스에는 별도 Google SDK, OAuth 클라이언트 또는 상용 UI 라이브러리를 포함하지 않습니다.
구글 시트는 Windows/.NET의 HTTP 기능으로 읽습니다. Google 서비스 이용 조건은 별도로 적용됩니다.
사용자가 설치한 글꼴은 프로그램에 포함하여 배포하지 않습니다.

재배포 시 자체 소스의 저작권·라이선스와 포함되는 외부 구성요소의 고지를 함께 보존하세요.
런타임 버전을 교체하여 배포하는 경우 실제 포함한 버전의 라이선스·제3자 고지도 확인하세요.
