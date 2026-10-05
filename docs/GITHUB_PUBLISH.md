# MyToDo 1.1.2 GitHub 공개 순서

저장소: https://github.com/lottoria-dev/MyToDoWidget
릴리즈 태그: **`1.1.2`** (앞에 `v`를 붙이지 않습니다.)

## 소스 반영과 Windows 확인

1. GitHub용 ZIP을 풀고 저장소 최상위에 소스를 반영합니다. `MyToDoWidget.sln`, `README.md`, `LICENSE.md`가 최상위에 위치하도록 합니다.
2. 기존 저장소의 Git 기록은 보존합니다. Visual Studio의 Git 변경 사항에서 수정 파일을 검토하고 커밋·푸시합니다.
3. `MyToDoWidget.sln`의 Release 빌드와 `run_checks.bat`를 실행합니다.
4. `docs/TESTING_KO.md`의 화면·공유 링크·자동 실행·잠금/절전 확인을 수행합니다.
5. 결과와 남은 제한 사항을 `VALIDATION.md`에 갱신합니다.
6. `build_release.bat`를 실행하면 standalone 실행용 ZIP과 체크섬을 만듭니다. Visual Studio의 Standalone-win-x64 게시 프로필도 정식판으로 설정되어 있습니다.

## GitHub Release

1. **Releases → Draft a new release**에서 검증한 커밋을 대상으로 `1.1.2` 태그를 만듭니다.
2. 제목은 **MyToDo 1.1.2**, 본문은 `docs/RELEASE_1.1.2.md`를 사용합니다.
3. 다음 파일을 첨부합니다. 홈페이지 링크는 실행용 파일명을 기준으로 합니다.

| 파일 | 용도 |
|---|---|
| `MyToDoWidget-1.1.2-win-x64.zip` | 사용자가 실행할 standalone 배포본 |
| `MyToDoWidget-1.1.2-win-x64.zip.sha256` | 위 실행용 ZIP의 SHA-256 |
| `MyToDoWidget_1.1.2_GitHub.zip` | 선택적으로 첨부할 소스 묶음 |

GitHub의 자동 Source code ZIP도 제공되므로 소스 묶음 첨부는 선택 사항입니다.
공개 릴리즈로 게시한 후 실행용 ZIP과 체크섬의 실제 다운로드를 확인하세요.

## 홈페이지 적용

`web/mytodo.html`은 기존 MathTime SPA에 삽입하는 콘텐츠 조각이며 완전한 독립 HTML 문서가 아닙니다.
기존 `dday.html`과 같은 위치에 올려 `https://mathtime.kr/?page=mytodo`에서 사용합니다.
기존 `/js/redirect.js` 연결과 `data-page-key="mytodo"`를 유지했습니다.

현재 페이지는 태그 `1.1.2`와 실행용 파일의 직접 다운로드 주소로 완성되어 있습니다.
**GitHub에 실행용 ZIP·체크섬을 올린 다음 홈페이지에 적용하세요.**
업로드 전 임시로 적용해야 하면 `#mytodo-page`의 `data-release-ready`를 `false`로 바꾸면 버튼이 Releases 목록으로 연결됩니다.

최종 링크:

- Release: https://github.com/lottoria-dev/MyToDoWidget/releases/tag/1.1.2
- ZIP: https://github.com/lottoria-dev/MyToDoWidget/releases/download/1.1.2/MyToDoWidget-1.1.2-win-x64.zip
- SHA-256: https://github.com/lottoria-dev/MyToDoWidget/releases/download/1.1.2/MyToDoWidget-1.1.2-win-x64.zip.sha256

이번 소스 제작 과정에서는 실행 파일 생성, GitHub 푸시·릴리즈 게시, 홈페이지 업로드를 수행하지 않았습니다.
