# 외부 구성요소 고지

MyToDo2 자체 소스와 함께 작성한 Apps Script 코드는 [LICENSE.md](LICENSE.md)의 MIT 라이선스입니다. 소스 묶음에는 실행용 런타임 바이너리를 포함하지 않으며 실행용 standalone 묶음에는 .NET과 Windows Desktop 10.0.13을 포함합니다. 외부 UI/Google SDK 패키지는 사용하지 않습니다.

| 구성요소 | 사용 | 고지 |
|---|---|---|
| Microsoft .NET 10 Runtime | C# 실행 기반 | `licenses/DOTNET-RUNTIME-LICENSE.txt` |
| Windows Desktop: WPF / Windows Forms | 위젯·설정·트레이 | `licenses/DOTNET-WINDOWSDESKTOP-LICENSE.txt` |
| .NET의 제3자 구성요소 | 자체 포함 빌드의 의존 구성요소 | `licenses/DOTNET-THIRD-PARTY-NOTICES.txt` |

포함한 라이선스와 고지는 NuGet 런타임 10.0.13 패키지 기준입니다. 런타임을 바꿔 다시 게시할 때 해당 버전의 고지를 함께 갱신해야 합니다.

Google Sheets / Apps Script는 사용자의 계정에 설치·배포하는 외부 서비스입니다. Google SDK나 Google OAuth 클라이언트 라이브러리는 포함하지 않습니다. Google 서비스 이용 조건은 별도로 적용됩니다.

소스 묶음에는 Node 런타임·제3자 npm 패키지·사용자 설치 글꼴을 포함하지 않습니다. 재배포 시 자체 소스의 저작권·라이선스와 실제 포함하는 외부 구성요소의 고지를 보존하세요.
