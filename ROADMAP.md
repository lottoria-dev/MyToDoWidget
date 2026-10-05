# MyToDo 개발 로드맵

제작: MathTime · 저장소: https://github.com/lottoria-dev/MyToDoWidget

## 1.x: 단순한 일반 공유 링크 읽기

- Google Sheets의 ‘링크가 있는 모든 사용자 · 뷰어’를 사용합니다.
- 위젯에서 OAuth 로그인 없이 메모를 읽습니다. Google 로그인은 시트 편집 시 브라우저에서 합니다.
- 설정·간편 사용법에 민감한 메모 입력을 피하도록 권장하고, 링크를 아는 사람은 읽을 수 있다는 이유를 설명합니다.
- A열의 자유 메모와 수동 새로고침을 유지합니다. 날짜 정렬·todo/done 관리는 별도 개선 과제입니다.
- 주소와 마지막 메모의 Windows 사용자별 암호화, 오류 내용 정리, 연결 실패 시 기존 메모 유지에 집중합니다.
- 일반 공유 링크를 개인정보 비공개 보관 기능으로 설명하지 않습니다.

## 이후 주요 업데이트: 사용자 소유 Apps Script 연결

전환 목표는 원본 Google Sheets를 ‘제한됨’으로 유지하면서, 위젯에서 필요한 메모만 읽는 것입니다.
적용 시점과 버전 번호는 실제 설치 과정 검증 후 확정합니다. 1.x 소스에는 Apps Script 기능을 넣지 않습니다.

1. 사용자의 Google 계정에 설치하는 Apps Script 템플릿을 제공합니다.
2. 사용자가 최초 권한 승인과 웹 앱 배포를 완료합니다. 소유자 권한으로 시트를 읽습니다.
3. 배포 주소와 별도의 충분히 긴 무작위 접근키를 위젯에 등록합니다.
4. 키는 URL에 넣지 않고 HTTPS 요청 본문으로 전달하며, 키 확인을 통과한 요청에만 지정된 메모 범위를 반환합니다.
5. 외부 요청에서 임의의 파일 ID·탭·범위를 지정하지 못하게 합니다. Google 접근 토큰을 위젯에 전달하지 않습니다.
6. 키 교체·접근 해제, 잘못된 키 응답, 사용량 제한, 조직 계정 정책을 검증합니다.
7. 다중 PC에서 한 번 설정한 뒤 사용하는 절차를 짧게 만들고, 실사용 시험 후 전환 안내를 제공합니다.

### 전환 시 확인할 사항

- 기존 ‘웹에 게시’를 중지하고 일반 공유 권한을 ‘제한됨’으로 바꿔야 원본의 공개 접근을 차단할 수 있습니다.
- 앱 연결을 바꾸는 것만으로 Google의 기존 게시·공유 권한이 바뀌지는 않습니다.
- Apps Script도 최초 Google 권한 승인이 필요합니다. 위젯의 OAuth 구현을 줄이는 방식입니다.
- 주소만 유출되면 읽지 못하도록 설계하지만, 주소와 접근키가 함께 유출되면 메모를 읽을 수 있습니다.
- 사용자 본인의 스크립트가 데이터를 반환하며, MathTime 운영 서버에 메모를 보관하는 구조를 추가하지 않습니다.
- 공개 링크와 Apps Script 방식의 차이, 지원 정책과 복구 절차를 설치 안내와 설정에 명확히 표시합니다.

## 근거 문서

- 공유와 뷰어 권한: https://support.google.com/drive/answer/2494822?hl=ko
- 비공개 시트 인증 / 링크 공유 시트: https://developers.google.com/chart/interactive/docs/spreadsheets
- Apps Script 웹 앱 실행 권한: https://developers.google.com/apps-script/guides/web
- Apps Script 최초 권한 승인: https://developers.google.com/apps-script/guides/services/authorization
