/* MyToDo2 2.0.1 — MathTime
 * Deploy: execute as the owner; allow requests without Google sign-in.
 * Keep the spreadsheet restricted. Each request is authenticated with a long key.
 * Only the configured spreadsheet is opened. No external service or token relay.
 */
var MYTODO_PROTOCOL = 3;
var MYTODO_VERSION = '2.0.1';
var RECORDS = 'MyToDo';
var OPERATIONS = '_MyToDoOperations';
var INFO = '_MyToDoInfo';
var ORDER_RECORDS = '_MyToDoOrder';
var ORDER_HEADERS = ['batchId', 'kind', 'chunk', 'ids'];
var RECORD_HEADERS = ['본문', '표시 제목', '출처', '원문 링크', '종류', '완료 상태', '_id', '_version', '_metadata', '_lastOperation', '위젯 표시', '보관 일시', '항목 배경색', '날짜·시각 (자동)'];
var OP_HEADERS = ['operationId', 'requestMetadata', 'requestBody', 'resultMetadata', 'resultBody', 'createdAt'];

// Example records are added once to a new personal installation.
// Existing user records and connection keys are never replaced by setup.
function templateExampleRows_() {
  return [
    ['다음 수업 준비\n· 활동지 25부 출력\n· 색연필과 자 준비\n\n아이디어: 마지막 5분에 오늘 배운 내용을 한 문장으로 적기\n#수업 #준비\n@교무부', '', '자유 메모 예시', '', '메모', '진행', '', '', '', ''],
    ['읽고 싶은 책 / 떠오른 생각\n수학의 역사 — 도서관에서 찾아보기\n\n문장, 목록, 빈 줄을 자유롭게 넣어 보세요. 날짜나 제목 없이 본문만 사용해도 됩니다.\n#독서 #아이디어', '', '자유 메모 예시', '', '메모', '진행', '', '', '', ''],
    ['장소: 상담실\n준비: 상담 기록과 학습 계획표\n상담 후 다음 주 실천할 목표를 한 가지 함께 정하기\n!날짜 2026-11-02\n!시간 15:30\n#상담 #행사\n@담임', '학생 상담', '일정 예시 · 날짜를 바꿔 사용하세요', '', '일정', '진행', '', '', '', ''],
    ['장소: 교육지원청\n준비: 신분증, 노트북, 연수 안내문\n교통편과 출발 시간 확인하기\n!날짜 2026-11-06\n#출장 #행사\n@교육지원청', '교육 연수 출장', '종일 일정 예시 · 날짜를 바꿔 사용하세요', '', '일정', '진행', '', '', '', ''],
    ['작성한 파일의 표와 오탈자 확인\n제출처: 업무 담당자\n!날짜 2026-11-09\n완료한 뒤 ✓를 누르면 완료 표시와 제목 삭제선을 확인할 수 있습니다.\n#보고서 #마감\n@업무 담당자', '보고서 제출', '마감 예시 · 날짜를 바꿔 사용하세요', '', '일정', '진행', '', '', '', '']
  ].map(function(row) { return row.concat(['표시', '', '', '']); });
}
function setupMyToDo() {
  var lock = LockService.getScriptLock();
  if (!lock.tryLock(5000)) throw new Error('처리 중입니다. 잠시 뒤 다시 실행하세요.');
  try {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    if (!ss) throw new Error('내 Google 시트에서 확장 프로그램 → Apps Script를 열고 실행하세요.');
    var props = PropertiesService.getScriptProperties();
    var records = ss.getSheetByName(RECORDS);
    var addExamples = (!records || records.getLastRow() <= 1)
      && !props.getProperty('MYTODO_INSTALL_VERSION') && !props.getProperty('MYTODO_DATASET_ID');
    formatMyToDoSheets_(ss);
    if (addExamples) {
      var examples = templateExampleRows_();
      literalRow(ss.getSheetByName(RECORDS).getRange(2, 1, examples.length, RECORD_HEADERS.length), examples);
    }
    var info = ss.getSheetByName(INFO);
    literalRow(info.getRange(2, 1, 1, 2), [['통신 규격', String(MYTODO_PROTOCOL)]]);
    literalRow(info.getRange(5, 1, 1, 2), [['설치 방식', '개인 시트에서 직접 설치']]);
    var guide = ensureSheet(ss, '시작 안내', ['MyToDo2 개인 시트', '사용 안내']);
    literalRow(guide.getRange(2, 1, 8, 2), [
      ['1. 설치 완료', '이 시트는 내 계정에서 직접 준비했습니다. 공유는 제한됨으로 유지합니다.'],
      ['2. 웹 앱 배포', 'Apps Script → 배포 → 새 배포 → 유형 선택 톱니바퀴 → 웹 앱. 실행: 나 / 액세스: 모든 사용자 → 배포. 본인 계정으로 권한 승인.'],
      ['3. URL 복사', '배포 완료 창: 웹 앱 → URL → 복사. 끝이 /exec인 URL을 사용합니다. 배포 ID는 사용하지 않습니다.'],
      ['4. 전용 코드', '이 시트의 MyToDo2 → 위젯 연결 → URL 붙여넣기 → 전용 코드 만들기 → 전용 코드 복사. 위젯의 마지막 단계에 붙여넣고 연결합니다.'],
      ['예시와 실제 메모', '자유 메모 2개·학생 상담·출장·보고서 마감 예시의 내용과 날짜를 고쳐 사용하세요. setupMyToDo를 다시 실행해도 실제 메모와 연결 키는 유지합니다.'],
      ['본문 표기', '#태그, 줄 처음의 @출처, >링크. 날짜는 !날짜 2026-11-02, 시각은 !시간 15:30을 별도 줄에 입력합니다.'],
      ['동기화', '편집 저장 후 자동 전송. 다른 PC의 변경은 기본 30분마다 확인. 즉시 확인: 위젯 ↻ / F5. 다른 PC도 같은 전용 코드를 사용합니다.'],
      ['코드 보관', '연결 정보는 암호처럼 보관하세요. 위젯에서 삭제한 항목은 이 시트에 보관됩니다. 연결 키는 원문으로 저장하지 않습니다.']
    ]);
    guide.setColumnWidth(1, 150); guide.setColumnWidth(2, 560); guide.getRange('A:B').setWrap(true);
    props.setProperties({ MYTODO_INSTALL_VERSION: MYTODO_VERSION });
    ss.setActiveSheet(guide); SpreadsheetApp.flush();
    onOpen();
    console.log('MyToDo2 설치 완료. 다음: 배포 → 새 배포 → 웹 앱. 배포 후 시트의 MyToDo2 → 위젯 연결을 엽니다.');
    return '설치 완료';
  } finally { lock.releaseLock(); }
}

function onOpen() {
  SpreadsheetApp.getUi().createMenu('MyToDo2')
    .addItem('메모·일정 입력', 'openMyToDoRecords')
    .addItem('시작 안내', 'openMyToDoGuide').addSeparator()
    .addItem('위젯 연결', 'showMyToDoSetup')
    .addItem('연결 키 폐기', 'revokeMyToDoKey').addToUi();
}
function openMyToDoRecords() { openMyToDoSheet_(RECORDS); }
function openMyToDoGuide() { openMyToDoSheet_('시작 안내'); }
function openMyToDoSheet_(name) {
  var ss = SpreadsheetApp.getActiveSpreadsheet(), sheet = ss && ss.getSheetByName(name);
  if (!sheet) { SpreadsheetApp.getUi().alert('Apps Script에서 setupMyToDo를 먼저 실행하세요.'); return; }
  ss.setActiveSheet(sheet);
}

function showMyToDoSetup() {
  SpreadsheetApp.getUi().showModalDialog(
    HtmlService.createHtmlOutput(connectionPageHtml_()).setWidth(530).setHeight(610), 'MyToDo2 위젯 연결');
}

function formatMyToDoSheets_(ss) {
  var records = ensureSheet(ss, RECORDS, RECORD_HEADERS);
  ensureSheet(ss, OPERATIONS, OP_HEADERS);
  var info = ensureSheet(ss, INFO, ['항목', '값']);
  records.setFrozenRows(1); records.setColumnWidth(1, 420);
  records.getRange('A:D').setWrap(true); records.hideColumns(7, 4);
  records.setColumnWidth(11, 90); records.setColumnWidth(12, 175); records.setColumnWidth(13, 100); records.setColumnWidth(14, 175);
  ss.getSheetByName(OPERATIONS).hideSheet(); info.hideSheet();
  ensureOrderStore_(ss);
}
function personalSheet_(ss) {
  var info = ss && ss.getSheetByName(INFO);
  if (!ss || !info || !ss.getSheetByName(RECORDS) || !ss.getSheetByName(OPERATIONS)
      || Number(info.getRange(2, 2, 1, 1).getValues()[0][0]) !== MYTODO_PROTOCOL)
    throw setupError_('SETUP_REQUIRED', 'Apps Script에서 setupMyToDo를 먼저 실행하세요.');
  return ss;
}

function setupError_(code, message) { var error = new Error(message); error.setupCode = code; return error; }
function setupFailure_(error) {
  return { ok: false, error: error.setupCode || 'SETUP_FAILED',
    message: error.setupCode ? error.message : '연결 준비에 실패했습니다. 개인 시트의 권한·계정과 Google 실행 상태를 확인하고 다시 시도하세요.' };
}
function currentSetup_(ss, props) {
  return props.getProperty('MYTODO_SHEET_ID') === ss.getId()
    && props.getProperty('MYTODO_SCRIPT_ID') === ScriptApp.getScriptId()
    && isId(props.getProperty('MYTODO_DATASET_ID'));
}
function getMyToDoSetupState(key) {
  try {
    var ss = personalSheet_(SpreadsheetApp.getActiveSpreadsheet());
    var props = PropertiesService.getScriptProperties();
    var current = currentSetup_(ss, props);
    return { ok: true, configured: current && !!props.getProperty('MYTODO_KEY_HASH'),
      datasetId: current ? props.getProperty('MYTODO_DATASET_ID') : null,
      endpoint: null, serverVersion: MYTODO_VERSION,
      keyMatches: current && typeof key === 'string' && key.length <= 128 && !!props.getProperty('MYTODO_KEY_HASH')
        && constantEqual(digest(key), props.getProperty('MYTODO_KEY_HASH')) };
  } catch (error) { return setupFailure_(error); }
}
// Invoked from the owner's sheet dialog. The browser generates a 256-bit key.
// No plaintext credential is stored in sheet cells or script properties.
function initializeMyToDoCopy(key, rotate) {
  var lock = null;
  try {
    if (typeof key !== 'string' || !/^[A-Za-z0-9_-]{43,128}$/.test(key))
      throw setupError_('INVALID_KEY', '연결 창에서 새 키를 생성하고 다시 시도하세요.');
    lock = LockService.getScriptLock();
    if (!lock.tryLock(5000)) { lock = null; throw setupError_('BUSY', '처리 중입니다. 잠시 뒤 다시 시도하세요.'); }
    var ss = personalSheet_(SpreadsheetApp.getActiveSpreadsheet());
    var props = PropertiesService.getScriptProperties(), current = currentSetup_(ss, props);
    var oldHash = current ? props.getProperty('MYTODO_KEY_HASH') : null;
    if (oldHash && !constantEqual(oldHash, digest(key)) && rotate !== true)
      throw setupError_('ALREADY_CONFIGURED', '이미 연결된 시트입니다. 보관한 연결 정보를 사용하거나 새 키로 재설정을 선택하세요.');
    var datasetId = current ? props.getProperty('MYTODO_DATASET_ID') : newId();
    formatMyToDoSheets_(ss);
    literalRow(ss.getSheetByName(INFO).getRange(2, 1, 3, 2), [
      ['통신 규격', String(MYTODO_PROTOCOL)], ['저장소 ID', datasetId], ['최근 연결 확인', '']
    ]);
    props.setProperties({ MYTODO_SHEET_ID: ss.getId(), MYTODO_SCRIPT_ID: ScriptApp.getScriptId(),
      MYTODO_DATASET_ID: datasetId, MYTODO_KEY_HASH: digest(key) });
    SpreadsheetApp.flush();
    return { ok: true, configured: true, keyMatches: true, datasetId: datasetId, endpoint: null, serverVersion: MYTODO_VERSION };
  } catch (error) { return setupFailure_(error); }
  finally { if (lock) lock.releaseLock(); }
}

function revokeMyToDoKey() {
  var ui = SpreadsheetApp.getUi();
  if (ui.alert('연결 키 폐기', '등록된 모든 PC의 연결이 중단됩니다. 메모는 삭제되지 않습니다.', ui.ButtonSet.OK_CANCEL) !== ui.Button.OK) return;
  var lock = LockService.getScriptLock();
  if (!lock.tryLock(5000)) { ui.alert('처리 중입니다. 잠시 뒤 다시 실행하세요.'); return; }
  try { PropertiesService.getScriptProperties().deleteProperty('MYTODO_KEY_HASH'); }
  finally { lock.releaseLock(); }
  ui.alert('키를 폐기했습니다. 시트의 MyToDo2 → 위젯 연결에서 다시 연결 정보를 만드세요.');
}

function doGet() { return jsonResponse({ ok: false, error: 'POST_REQUIRED' }); }
function doPost(e) {
  var lock = null;
  try {
    if (!e || !e.postData || e.postData.contents.length > 384000) return fail('INVALID');
    var request;
    try { request = JSON.parse(e.postData.contents); } catch (_) { return fail('INVALID'); }
    if (!request || request.protocol !== MYTODO_PROTOCOL) return fail('PROTOCOL');
    var props = PropertiesService.getScriptProperties();
    var sheetId = props.getProperty('MYTODO_SHEET_ID');
    var datasetId = props.getProperty('MYTODO_DATASET_ID');
    var keyHash = props.getProperty('MYTODO_KEY_HASH');
    if (!sheetId || !datasetId || props.getProperty('MYTODO_SCRIPT_ID') !== ScriptApp.getScriptId()) return fail('NOT_CONFIGURED');
    if (!keyHash || typeof request.key !== 'string' || request.key.length > 128
      || !constantEqual(digest(request.key), keyHash)) return fail('AUTH');
    if (['probe', 'snapshot', 'mutate', 'reorder'].indexOf(request.action) < 0) return fail('INVALID');
    if (request.action !== 'probe' && request.datasetId !== datasetId) return fail('DATASET');
    lock = LockService.getScriptLock();
    if (!lock.tryLock(5000)) { lock = null; return fail('BUSY'); }
    // Recheck after acquiring the lock: setup may rotate the key while a request waits.
    var currentHash = props.getProperty('MYTODO_KEY_HASH');
    if (!currentHash || !constantEqual(digest(request.key), currentHash)) return fail('AUTH');
    if (sheetId !== props.getProperty('MYTODO_SHEET_ID') || datasetId !== props.getProperty('MYTODO_DATASET_ID')
      || props.getProperty('MYTODO_SCRIPT_ID') !== ScriptApp.getScriptId()) return fail('DATASET');
    var ss = SpreadsheetApp.openById(sheetId);
    var records = ss.getSheetByName(RECORDS);
    var operations = ss.getSheetByName(OPERATIONS);
    if (!records || !operations || !ss.getSheetByName(INFO)) return fail('NOT_CONFIGURED');
    ensureSheet(ss, RECORDS, RECORD_HEADERS);
    ensureOrderStore_(ss);
    recoverPending(records, operations, ss);
    var rows = readRecords(records, true);
    var answer = { ok: true, protocol: MYTODO_PROTOCOL, datasetId: datasetId, serverVersion: MYTODO_VERSION, serverTime: now() };
    if (request.action === 'probe') {
      // Verify write access without adding a visible test memo.
      literalRow(ss.getSheetByName(INFO).getRange(4, 2, 1, 1), [[now()]]);
      answer.capabilities = { read: true, write: true };
    } else if (request.action === 'snapshot') {
      answer.items = rows.map(function (r) { return wire(r.item); });
      answer.orders = readOrders_(ss);
    } else if (request.action === 'reorder') {
      var orderChange;
      try { orderChange = validateOrderChange_(request.change); } catch (_) { return fail('INVALID'); }
      answer.result = processOrderChange_(ss, records, operations, rows, orderChange);
      if (answer.result.error) return fail(answer.result.error);
    } else {
      var change;
      try { change = validateChange(request.change); } catch (_) { return fail('INVALID'); }
      answer.result = processChange(records, operations, rows, change);
      if (answer.result.error) return fail(answer.result.error);
    }
    SpreadsheetApp.flush();
    return jsonResponse(answer);
  } catch (_) {
    // Do not log request bodies, keys, file IDs, or raw Google errors.
    return fail('SERVER_ERROR');
  } finally { if (lock) lock.releaseLock(); }
}

function processChange(records, operations, rows, change) {
  var stored = readOperations(operations);
  var prior = stored.find(function (o) { return o.id === change.operationId; });
  var metadata = requestMetadata(change);
  if (prior) {
    if (prior.requestMetadata !== metadata || prior.requestBody !== change.item.body) return { error: 'OPERATION_REUSED' };
    return readResult(prior);
  }
  var number = operations.getLastRow() + 1;
  var op = { row: number, id: change.operationId, requestMetadata: metadata, requestBody: change.item.body, resultMetadata: '', resultBody: '', createdAt: now() };
  writeOperation(operations, op); SpreadsheetApp.flush(); // Durable intent before the record write.
  var result = applyChange(records, rows, change);
  finishOperation(operations, op, result);
  return result;
}

function recoverPending(records, operations, ss) {
  ss = ss || records.getParent();
  var pending = readOperations(operations).filter(function (o) { return !o.resultMetadata; });
  if (!pending.length) return;
  // A sheet edit made between failed invocations must also participate in version checks.
  var rows = readRecords(records, true);
  pending.forEach(function (op) {
    var stored = JSON.parse(op.requestMetadata);
    if (stored.type === 'order') {
      var orderChange = validateOrderChange_(stored.change);
      if (orderChange.operationId !== op.id) throw new Error('Invalid order operation');
      applyOrderChange_(ss, rows, orderChange);
      finishOperation(operations, op, { status: 'applied', operationId: op.id });
      return;
    }
    stored.operationId = op.id; stored.item.body = op.requestBody;
    var change = validateChange(stored);
    var row = rows.find(function (r) { return r.item.id === change.item.id; });
    var result;
    if (row && row.lastOperation === op.id) {
      // A previous invocation committed the record but did not commit its receipt.
      var committed = wire(row.item); committed.body = op.requestBody;
      result = { status: 'applied', item: committed };
    } else result = applyChange(records, rows, change);
    finishOperation(operations, op, result);
  });
}

function applyChange(records, rows, change) {
  var row = rows.find(function (r) { return r.item.id === change.item.id; });
  var version = row ? row.item.version : 0;
  if (version !== change.baseVersion || (row && row.item.isDeleted && !change.item.isDeleted))
    return { status: 'conflict', item: row ? wire(row.item) : null };
  var item = clone(change.item), stamp = now();
  item.version = version + 1;
  item.createdAt = row ? row.item.createdAt : stamp;
  item.updatedAt = stamp;
  item.completedAt = item.isCompleted ? (row && row.item.isCompleted ? row.item.completedAt : stamp) : null;
  item.deletedAt = item.isDeleted ? (row && row.item.isDeleted ? row.item.deletedAt : stamp) : null;
  var entry = { row: row ? row.row : records.getLastRow() + 1, item: item, lastOperation: change.operationId };
  writeRecord(records, entry); SpreadsheetApp.flush();
  if (row) { row.item = item; row.lastOperation = change.operationId; } else rows.push(entry);
  return { status: 'applied', item: wire(item) };
}

function readTextMetadata_(body) {
  var source = null, link = null;
  String(body).replace(/\r\n?/g, '\n').split('\n').forEach(function(line) {
    var value = line.trim();
    if (value.charAt(0) === '@' && value.length > 1) source = value.slice(1).trim();
    if (value.charAt(0) === '>') {
      var url = value.slice(1).trim();
      if (/^https?:\/\/[^/\s@]+(?:[/?#][^\s]*)?$/i.test(url)) link = url;
    }
  });
  return { source: source, link: link };
}
function readScheduleText_(body, zone) {
  var date = null, time = null, seenDate = false, seenTime = false, error = null;
  String(body).replace(/\r\n?/g, '\n').split('\n').forEach(function(raw) {
    var line = raw.trim(), m;
    if (/^!날짜(?:\s|$)/.test(line)) {
      if (seenDate) { error = '날짜 중복'; return; } seenDate = true;
      m = /^!날짜\s+(\d{4})(?:[-./]|년\s*)(\d{1,2})(?:[-./]|월\s*)(\d{1,2})일?$/.exec(line);
      if (!m) { error = '날짜 형식 확인'; return; }
      date = m[1] + '-' + ('0' + m[2]).slice(-2) + '-' + ('0' + m[3]).slice(-2);
      var parsed = new Date(date + 'T00:00:00Z');
      if (!Number.isFinite(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== date || Number(m[1]) < 1) error = '날짜 확인';
    } else if (/^!시간(?:\s|$)/.test(line)) {
      if (seenTime) { error = '시각 중복'; return; } seenTime = true;
      m = /^!시간\s+(\d{1,2}):(\d{2})$/.exec(line);
      if (!m || Number(m[1]) > 23 || Number(m[2]) > 59) { error = '시각 형식 확인'; return; }
      time = ('0' + m[1]).slice(-2) + ':' + m[2];
    }
  });
  if (time && !date) error = '날짜도 입력하세요';
  var instant = null;
  if (!error && date && time) {
    try {
      var wall = Date.parse(date + 'T' + time + ':00Z'), value = wall;
      for (var n = 0; n < 3; n++) {
        var offset = Utilities.formatDate(new Date(value), zone, 'Z');
        var minutes = (Number(offset.slice(1, 3)) * 60 + Number(offset.slice(3, 5))) * (offset[0] === '-' ? -1 : 1);
        value = wall - minutes * 60000;
      }
      var shown = date + ' ' + time;
      if (Utilities.formatDate(new Date(value), zone, 'yyyy-MM-dd HH:mm') !== shown) error = '시간대 전환 시각 확인';
      // Do not choose silently between two occurrences of a repeated wall time.
      var suffix = Utilities.formatDate(new Date(value), zone, 'Z');
      if (Utilities.formatDate(new Date(value - 10800000), zone, 'Z') !== suffix || Utilities.formatDate(new Date(value + 10800000), zone, 'Z') !== suffix)
        for (var delta = -180; delta <= 180 && !error; delta += 30)
          if (delta !== 0 && Utilities.formatDate(new Date(value + delta * 60000), zone, 'yyyy-MM-dd HH:mm') === shown) error = '시간대 중복 시각 확인';
      instant = date + 'T' + time + ':00' + suffix.slice(0, 3) + ':' + suffix.slice(3);
    } catch (_) { error = '시간대 확인'; }
  }
  return { date: error ? null : date, instant: error ? null : instant, zone: !error && instant ? zone : null, hasMarkers: seenDate || seenTime, error: error,
    role: !error && date ? (/(?:^|\s)#마감(?=$|\s|[,.;!?])/.test(body) ? 1 : 2) : 0 };
}
function applyBodyDate_(item, parsed) {
  item.whenDate = parsed.date; item.whenAt = parsed.instant; item.timeZoneId = parsed.zone; item.dateRole = parsed.role;
}
function sheetZone_(item, sheet) {
  if (item.timeZoneId === 'Korea Standard Time') return 'Asia/Seoul';
  if (item.timeZoneId && (item.timeZoneId.indexOf('/') >= 0 || ['UTC', 'GMT'].indexOf(item.timeZoneId) >= 0)) return item.timeZoneId;
  return sheet.getParent().getSpreadsheetTimeZone();
}
function normalizeCardColor_(value) {
  value = nullableText(value); if (value === null) return null;
  value = value.trim(); if (/^[0-9a-f]{6}$/i.test(value)) value = '#' + value;
  if (!/^#[0-9a-f]{6}$/i.test(value)) throw new Error('Invalid card color');
  return value.toUpperCase();
}
function readRecords(sheet, reconcile) {
  if (sheet.getLastRow() < 2) return [];
  if (sheet.getLastRow() > 10001) throw new Error('Too many records');
  var values = sheet.getRange(2, 1, sheet.getLastRow() - 1, RECORD_HEADERS.length).getValues();
  var ids = {};
  return values.map(function (cells, index) {
    if (!String(cells[8])) {
      if (cells.every(function (v) { return String(v) === ''; })) return null;
      if (cells.slice(6, 10).some(function (v) { return String(v) !== ''; })) throw new Error('Incomplete metadata');
      // A user can add a new free memo directly in the visible columns.
      var added = { id: newId(), kind: String(cells[4]) === '일정' ? 1 : 0, body: String(cells[0]),
        title: nullableText(cells[1]), cardColor: normalizeCardColor_(cells[12]), source: nullableText(cells[2]), originalUrl: nullableText(cells[3]),
        dateRole: 0, whenDate: null, whenAt: null, timeZoneId: null, reminderAt: null, reminderId: null,
        isCompleted: String(cells[5]) === '완료', completedAt: String(cells[5]) === '완료' ? now() : null,
        isDeleted: String(cells[10]) === '보관', deletedAt: String(cells[10]) === '보관' ? now() : null, createdAt: now(), updatedAt: now(), version: 1, conflictOf: null, conflictReason: null };
      applyBodyDate_(added, readScheduleText_(added.body, sheetZone_(added, sheet)));
      var addedText = readTextMetadata_(added.body);
      if (addedText.source !== null) added.source = addedText.source;
      if (addedText.link !== null) added.originalUrl = addedText.link;
      validateItem(added);
      var newEntry = { row: index + 2, item: added, lastOperation: newId() };
      writeRecord(sheet, newEntry); ids[added.id] = true; return newEntry;
    }
    var metadata = JSON.parse(String(cells[8]));
    var originalHash = metadata.bodyHash;
    var item = wire(metadata); item.body = String(cells[0]);
    if (ids[item.id]) throw new Error('Duplicate record'); ids[item.id] = true;
    var changed = originalHash !== digest(item.body);
    if (reconcile) {
      // Reconcile editable visible columns; hidden identity/version fields stay system-managed.
      var text = readTextMetadata_(item.body);
      var title = nullableText(cells[1]);
      var source = text.source !== null ? text.source : metadata.sourceFromText === true ? null : nullableText(cells[2]);
      var url = text.link !== null ? text.link : metadata.linkFromText === true ? null : nullableText(cells[3]);
      var kind = String(cells[4]) === '일정' ? 1 : String(cells[4]) === '메모' ? 0 : -1;
      var completed = String(cells[5]) === '완료';
      // Blank archive state occurs only when reading an older ten-column preview.
      var archived = String(cells[10]) === '' ? item.isDeleted : String(cells[10]) === '보관';
      if (['', '표시', '보관'].indexOf(String(cells[10])) < 0) throw new Error('Invalid archive state');
      var cardColor = normalizeCardColor_(cells[12]);
      var dates = readScheduleText_(item.body, sheetZone_(item, sheet));
      if (dates.hasMarkers || metadata.dateFromText === true) {
        changed = changed || item.whenDate !== dates.date || item.whenAt !== dates.instant || item.dateRole !== dates.role;
        applyBodyDate_(item, dates);
      }
      changed = changed || item.isDeleted !== archived || item.cardColor !== cardColor;
      if (item.isDeleted !== archived) item.deletedAt = archived ? now() : null;
      item.isDeleted = archived; item.cardColor = cardColor;
      if (kind < 0 || ['완료', '진행'].indexOf(String(cells[5])) < 0) throw new Error('Invalid visible fields');
      changed = changed || item.title !== title || item.source !== source || item.originalUrl !== url || item.kind !== kind || item.isCompleted !== completed;
      item.title = title; item.source = source; item.originalUrl = url; item.kind = kind;
      if (item.isCompleted !== completed) {
        item.completedAt = completed ? now() : null;
        if (!completed && item.reminderAt) item.reminderId = newId();
      }
      item.isCompleted = completed;
    }
    validateItem(item);
    var entry = { row: index + 2, item: item, lastOperation: String(cells[9]) };
    if (reconcile && changed) {
      item.version++; item.updatedAt = now(); entry.lastOperation = newId(); writeRecord(sheet, entry);
    } else if (reconcile && (String(cells[10]) === '' || String(cells[11]) !== (item.deletedAt || '') || String(cells[13]) !== dateDisplay_(item, dates, sheet))) writeRecord(sheet, entry);
    return entry;
  }).filter(function (entry) { return entry !== null; });
}

function dateDisplay_(item, parsed, sheet) {
  return parsed.error || (item.whenDate ? item.whenDate + (item.whenAt ? ' ' + Utilities.formatDate(new Date(item.whenAt), sheetZone_(item, sheet), 'HH:mm') : '') : '');
}
function writeRecord(sheet, entry) {
  var item = entry.item, metadata = wire(item);
  delete metadata.body; metadata.bodyHash = digest(item.body);
  var text = readTextMetadata_(item.body);
  metadata.sourceFromText = text.source !== null;
  metadata.linkFromText = text.link !== null;
  var dateText = readScheduleText_(item.body, sheetZone_(item, sheet));
  metadata.dateFromText = dateText.hasMarkers;
  var values = [item.body, item.title || '', item.source || '', item.originalUrl || '',
    item.kind === 0 ? '메모' : '일정', item.isCompleted ? '완료' : '진행',
    item.id, String(item.version), JSON.stringify(metadata), entry.lastOperation, item.isDeleted ? '보관' : '표시', item.deletedAt || '', item.cardColor || '', dateDisplay_(item, dateText, sheet)];
  literalRow(sheet.getRange(entry.row, 1, 1, RECORD_HEADERS.length), [values]);
}
function readOperations(sheet) {
  if (sheet.getLastRow() < 2) return [];
  return sheet.getRange(2, 1, sheet.getLastRow() - 1, 6).getValues().map(function (v, i) {
    return { row: i + 2, id: String(v[0]), requestMetadata: String(v[1]), requestBody: String(v[2]),
      resultMetadata: String(v[3]), resultBody: String(v[4]), createdAt: String(v[5]) };
  });
}
function writeOperation(sheet, op) {
  literalRow(sheet.getRange(op.row, 1, 1, 6), [[op.id, op.requestMetadata, op.requestBody, op.resultMetadata, op.resultBody, op.createdAt]]);
}
function finishOperation(sheet, op, result) {
  var metadata = clone(result);
  op.resultBody = metadata.item ? metadata.item.body : '';
  if (metadata.item) delete metadata.item.body;
  op.resultMetadata = JSON.stringify(metadata); writeOperation(sheet, op); SpreadsheetApp.flush();
}
function readResult(op) {
  var result = JSON.parse(op.resultMetadata);
  if (result.item) result.item.body = op.resultBody;
  return result;
}
function requestMetadata(change) {
  var value = { baseVersion: change.baseVersion, item: clone(change.item) };
  delete value.item.body; return JSON.stringify(value);
}
function validateChange(change) {
  if (!change || !isId(change.operationId) || !Number.isSafeInteger(change.baseVersion) || change.baseVersion < 0) throw new Error('Invalid');
  validateItem(change.item);
  return { operationId: change.operationId, baseVersion: change.baseVersion, item: wire(change.item) };
}
function validateItem(item) {
  if (!item || !isId(item.id) || [0, 1].indexOf(item.kind) < 0 || [0, 1, 2].indexOf(item.dateRole) < 0
    || !Number.isSafeInteger(item.version) || item.version < 0 || typeof item.body !== 'string'
    || !item.body.trim() || item.body.length > 40000 || typeof item.isCompleted !== 'boolean' || typeof item.isDeleted !== 'boolean') throw new Error('Invalid');
  optionalString(item.cardColor, 7);
  if (item.cardColor != null && !/^#[0-9A-F]{6}$/.test(item.cardColor)) throw new Error('Invalid card color');
  optionalString(item.title, 200); optionalString(item.source, 500); optionalString(item.originalUrl, 2048);
  optionalString(item.timeZoneId, 128); optionalString(item.conflictReason, 500);
  if (item.originalUrl && (!/^https?:\/\/[^\/\s]+/i.test(item.originalUrl) || /^https?:\/\/[^\/]*@/i.test(item.originalUrl))) throw new Error('Invalid URL');
  if (item.whenDate && (!/^\d{4}-\d{2}-\d{2}$/.test(item.whenDate)
    || new Date(item.whenDate + 'T00:00:00Z').toISOString().slice(0, 10) !== item.whenDate)) throw new Error('Invalid date');
  if (item.dateRole === 0 && (item.whenDate || item.whenAt)) throw new Error('Invalid date');
  if (item.whenAt && (!item.whenDate || !validInstant(item.whenAt))) throw new Error('Invalid time');
  if (item.reminderAt && (!validInstant(item.reminderAt) || !isId(item.reminderId))) throw new Error('Invalid reminder');
  if (item.conflictOf && !isId(item.conflictOf)) throw new Error('Invalid conflict');
  if (!validInstant(item.createdAt) || !validInstant(item.updatedAt)) throw new Error('Invalid timestamp');
}
function wire(item) {
  var result = {};
  ['id', 'kind', 'body', 'title', 'cardColor', 'source', 'originalUrl', 'dateRole', 'whenDate', 'whenAt', 'timeZoneId',
    'reminderAt', 'reminderId', 'isCompleted', 'completedAt', 'isDeleted', 'deletedAt', 'createdAt', 'updatedAt',
    'version', 'conflictOf', 'conflictReason'].forEach(function (key) {
      if (Object.prototype.hasOwnProperty.call(item, key)) result[key] = item[key];
      else if (['title', 'cardColor', 'source', 'originalUrl', 'whenDate', 'whenAt', 'timeZoneId', 'reminderAt', 'reminderId',
        'completedAt', 'deletedAt', 'conflictOf', 'conflictReason'].indexOf(key) >= 0) result[key] = null;
    });
  return result;
}
function ensureSheet(ss, name, headers) {
  var sheet = ss.getSheetByName(name);
  if (!sheet) sheet = ss.insertSheet(name);
  if (sheet.getLastRow() === 0) literalRow(sheet.getRange(1, 1, 1, headers.length), [headers]);
  else if (sheet.getRange(1, 1, 1, headers.length).getValues()[0].join('|') !== headers.join('|')) {
    var old = sheet.getRange(1, 1, 1, headers.length).getValues()[0];
    if (name !== RECORDS || old.slice(0, 10).join('|') !== headers.slice(0, 10).join('|') || old.slice(10).some(function(v) { return String(v) !== ''; })) throw new Error('Unexpected sheet schema');
    literalRow(sheet.getRange(1, 1, 1, headers.length), [headers]);
    sheet.setColumnWidth(11, 90); sheet.setColumnWidth(12, 175); sheet.setColumnWidth(13, 100); sheet.setColumnWidth(14, 175);
  }
  return sheet;
}
function literalRow(range, rows) {
  // Rich text stores strings literally, including leading "=" and multiline pasted text.
  range.setRichTextValues(rows.map(function (row) {
    return row.map(function (value) { return SpreadsheetApp.newRichTextValue().setText(String(value)).build(); });
  }));
}
function optionalString(value, max) { if (value != null && (typeof value !== 'string' || value.length > max)) throw new Error('Invalid string'); }
function nullableText(value) { var s = String(value); return s.length ? s : null; }
function validInstant(value) { return typeof value === 'string' && /T.*(Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value)); }
function isId(value) { return typeof value === 'string' && /^[0-9a-f]{32}$/.test(value); }
function newId() { return Utilities.getUuid().replace(/-/g, '').toLowerCase(); }
function now() { return new Date().toISOString(); }
function clone(value) { return JSON.parse(JSON.stringify(value)); }
function digest(value) {
  return Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256, value, Utilities.Charset.UTF_8)
    .map(function (b) { return ('0' + ((b + 256) % 256).toString(16)).slice(-2); }).join('');
}
function constantEqual(a, b) {
  if (a.length !== b.length) return false;
  var different = 0; for (var i = 0; i < a.length; i++) different |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return different === 0;
}
function fail(code) { return jsonResponse({ ok: false, error: code }); }
function jsonResponse(value) { return ContentService.createTextOutput(JSON.stringify(value)).setMimeType(ContentService.MimeType.JSON); }


// Shared view order is independent of memo content/revisions. Long ID lists are chunked
// into a hidden append-only journal, below Sheets' per-cell text limit. A small head
// pointer commits a fully flushed batch; incomplete batches are never read as current.
function ensureOrderStore_(ss) {
  var sheet = ensureSheet(ss, ORDER_RECORDS, ORDER_HEADERS);
  sheet.hideSheet();
  return sheet;
}
function readOrderState_(ss, kind) {
  var raw = String(ss.getSheetByName(INFO).getRange(6 + kind, 2, 1, 1).getValues()[0][0]);
  if (!raw) return { ids: [], operationId: null };
  var head = JSON.parse(raw);
  if (!isId(head.batchId) || !isId(head.operationId) || !Number.isInteger(head.chunks) || head.chunks < 1 || head.chunks > 20
    || !Number.isSafeInteger(head.row) || head.row < 2)
    throw new Error('Invalid order head');
  var sheet = ss.getSheetByName(ORDER_RECORDS), chunks = [];
  if (!sheet || sheet.getLastRow() < head.row + head.chunks - 1) throw new Error('Missing order batch');
  sheet.getRange(head.row, 1, head.chunks, 4).getValues().forEach(function(row) {
    if (String(row[0]) !== head.batchId) return;
    var n = Number(row[2]);
    if (Number(row[1]) !== kind || !Number.isInteger(n) || n < 0 || n >= head.chunks || chunks[n])
      throw new Error('Invalid order chunk');
    var ids = JSON.parse(String(row[3]));
    if (!Array.isArray(ids) || ids.length > 500 || ids.some(function(id) { return !isId(id); })) throw new Error('Invalid order IDs');
    chunks[n] = ids;
  });
  var result = [];
  for (var i = 0; i < head.chunks; i++) {
    if (!chunks[i]) throw new Error('Incomplete order batch');
    result = result.concat(chunks[i]);
  }
  if (result.length > 10000 || new Set(result).size !== result.length) throw new Error('Invalid order');
  return { ids: result, operationId: head.operationId };
}
function readOrders_(ss) {
  return [0, 1].map(function(kind) { return { kind: kind, ids: readOrderState_(ss, kind).ids }; });
}
function writeOrderState_(ss, kind, ids, operationId) {
  if (ids.length > 10000) throw new Error('Too many order IDs');
  var sheet = ensureOrderStore_(ss), batchId = newId(), rows = [];
  for (var start = 0; start < Math.max(1, ids.length); start += 500)
    rows.push([batchId, String(kind), String(rows.length), JSON.stringify(ids.slice(start, start + 500))]);
  var firstRow = sheet.getLastRow() + 1, finalRow = firstRow + rows.length - 1;
  if (finalRow > sheet.getMaxRows())
    sheet.insertRowsAfter(sheet.getMaxRows(), Math.max(100, finalRow - sheet.getMaxRows()));
  literalRow(sheet.getRange(firstRow, 1, rows.length, 4), rows);
  SpreadsheetApp.flush(); // Immutable chunks first; the previous head still points to intact data.
  literalRow(ss.getSheetByName(INFO).getRange(6 + kind, 1, 1, 2), [[kind === 0 ? '메모 표시 순서' : '일정 표시 순서',
    JSON.stringify({ batchId: batchId, operationId: operationId, chunks: rows.length, row: firstRow })]]);
  SpreadsheetApp.flush(); // One small pointer commits the complete order.
}
function validateOrderChange_(change) {
  if (!change || !isId(change.operationId) || [0, 1].indexOf(change.kind) < 0
    || typeof change.reset !== 'boolean' || typeof change.after !== 'boolean'
    || (change.reset ? change.sourceId != null || change.targetId != null || change.after
      : !isId(change.sourceId) || !isId(change.targetId) || change.sourceId === change.targetId)) throw new Error('Invalid order move');
  return { operationId: change.operationId, kind: change.kind, sourceId: change.sourceId || null,
    targetId: change.targetId || null, after: change.after, reset: change.reset };
}
function defaultOrderedItems_(rows, kind) {
  return rows.map(function(row) { return row.item; }).filter(function(item) { return item.kind === kind; }).sort(function(a, b) {
    if (kind === 1) {
      var dateA = a.whenDate || '9999-12-31', dateB = b.whenDate || '9999-12-31';
      if (dateA !== dateB) return dateA < dateB ? -1 : 1;
    }
    var delta = Date.parse(a.createdAt) - Date.parse(b.createdAt);
    if (delta) return kind === 0 ? -delta : delta;
    return a.id === b.id ? 0 : a.id < b.id ? -1 : 1;
  });
}
function applyOrderChange_(ss, rows, change) {
  var state = readOrderState_(ss, change.kind);
  if (state.operationId === change.operationId) return; // Committed pointer with a missing receipt.
  var ids = [];
  if (!change.reset) {
    var defaults = defaultOrderedItems_(rows, change.kind), byId = {};
    defaults.forEach(function(item) { byId[item.id] = item; });
    var source = byId[change.sourceId], target = byId[change.targetId];
    // A concurrently archived/moved item is not resurrected by an old drag command.
    if (!source || !target || source.isDeleted || target.isDeleted) ids = state.ids;
    else {
      var present = {};
      state.ids.forEach(function(id) { if (byId[id] && !present[id]) { ids.push(id); present[id] = true; } });
      defaults.forEach(function(item) { if (!present[item.id]) { ids.push(item.id); present[item.id] = true; } });
      ids.splice(ids.indexOf(source.id), 1);
      ids.splice(ids.indexOf(target.id) + (change.after ? 1 : 0), 0, source.id);
    }
  }
  writeOrderState_(ss, change.kind, ids, change.operationId);
}
function processOrderChange_(ss, records, operations, rows, change) {
  var metadata = JSON.stringify({ type: 'order', change: change });
  var previous = readOperations(operations).find(function(op) { return op.id === change.operationId; });
  if (previous) {
    if (previous.requestMetadata !== metadata || previous.requestBody !== '') return { error: 'OPERATION_REUSED' };
    return { status: 'applied', operationId: change.operationId, orders: readOrders_(ss) };
  }
  var op = { row: operations.getLastRow() + 1, id: change.operationId, requestMetadata: metadata,
    requestBody: '', resultMetadata: '', resultBody: '', createdAt: now() };
  writeOperation(operations, op); SpreadsheetApp.flush();
  applyOrderChange_(ss, rows, change);
  // Receipts contain only the operation ID, never a giant list in one cell.
  finishOperation(operations, op, { status: 'applied', operationId: change.operationId });
  return { status: 'applied', operationId: change.operationId, orders: readOrders_(ss) };
}

// Connection screen is embedded: no HTML or manifest file needs to be pasted.
function connectionPageHtml_() {
  return [
    "<!doctype html>",
    "<html lang=\"ko\">",
    "<head>",
    "  <base target=\"_blank\">",
    "  <meta charset=\"utf-8\">",
    "  <style>",
    "    body { margin: 20px; color: #263342; background: #fff; font: 14px/1.6 system-ui, 'Malgun Gothic', sans-serif; }",
    "    h1 { margin: 0 0 6px; font-size: 20px; } p { margin: 6px 0 12px; }",
    "    .muted { color: #627184; font-size: 12px; } .card { padding: 14px; margin: 12px 0; background: #eff7fc; border-radius: 10px; }",
    "    ol { padding-left: 22px; margin: 8px 0; } button { border: 0; border-radius: 7px; padding: 10px 13px; background: #00a0d7; color: white; cursor: pointer; font: inherit; margin: 4px 6px 4px 0; }",
    "    button.secondary { background: #e7edf3; color: #263342; } button:disabled { opacity: .45; cursor: default; }",
    "    input { box-sizing: border-box; width: 100%; padding: 9px; border: 1px solid #bac9d8; border-radius: 6px; }",
    "    #status { min-height: 38px; overflow-wrap: anywhere; } #status.error { color: #a52a2a; }",
    "    details { margin: 10px 0; } label { display: block; margin: 6px 0; }",
    "  </style>",
    "</head>",
    "<body>",
    "  <h1>내 시트와 위젯 연결</h1>",
    "  <p class=\"muted\">내 시트의 공유는 ‘제한됨’으로 유지하세요.</p>",
    "  <div id=\"deploymentGuide\" class=\"card\">",
    "    <strong>배포 화면의 웹 앱 URL을 사용하세요</strong>",
    "    <ol>",
    "      <li>시트 상단 <b>확장 프로그램 → Apps Script</b>. 오른쪽 위 <b>배포 → 새 배포</b>.</li>",
    "      <li><b>유형 선택</b> 옆 톱니바퀴 → <b>웹 앱</b>. 실행: <b>나</b> / 액세스: <b>모든 사용자</b> → <b>배포</b>. 요청되면 본인 계정으로 권한을 승인합니다.</li>",
    "      <li>완료 창의 <b>웹 앱 → URL → 복사</b>. 이 창으로 돌아와 아래에 붙여넣습니다.</li>",
    "    </ol>",
    "    <p class=\"muted\">이미 배포했다면 배포 → 배포 관리 → 해당 웹 앱 → URL → 복사. ‘배포 ID’는 사용하지 않습니다.</p>",
    "  </div>",
    "  <label for=\"endpoint\"><b>웹 앱 URL (/exec)</b></label>",
    "  <input id=\"endpoint\" type=\"password\" autocomplete=\"off\" maxlength=\"1024\" oninput=\"endpointChanged()\" placeholder=\"배포 완료 창의 URL을 붙여넣으세요\" aria-label=\"웹 앱 URL (/exec)\">",
    "  <p class=\"muted\">완료 창에서 복사한 URL을 붙여넣으세요.</p>",
    "  <div id=\"status\" role=\"status\" aria-live=\"polite\">설치한 시트의 연결 상태를 확인합니다…</div>",
    "  <button id=\"prepare\" onclick=\"prepare(false)\" disabled>전용 코드 만들기</button>",
    "  <button id=\"refresh\" class=\"secondary\" onclick=\"refresh()\">연결 상태 확인</button>",
    "  <button id=\"rotate\" class=\"secondary\" onclick=\"prepare(true)\" hidden disabled>새 키로 재설정</button>",
    "  <div id=\"ready\" class=\"card\" hidden>",
    "    <strong>위젯에 한 번 붙여넣기</strong>",
    "    <p>‘전용 코드 복사’를 누르고 위젯의 설정 → 시트 연결 → 4번에 붙여넣고 연결하기를 누르세요.</p>",
    "    <input id=\"connection\" type=\"password\" readonly autocomplete=\"off\" aria-label=\"새 연결 정보\">",
    "    <button id=\"copy\" onclick=\"copyConnection()\">전용 코드 복사</button>",
    "    <button id=\"download\" class=\"secondary\" onclick=\"downloadConnection()\">코드 파일 보관</button>",
    "    <p class=\"muted\">다른 PC도 같은 연결 정보를 사용합니다. 이 창을 닫으면 새 키는 다시 표시할 수 없으므로 먼저 보관하세요. 연결 정보는 암호와 같으며 파일은 암호화되지 않습니다.</p>",
    "  </div>",
    "  <script>",
    "    'use strict';",
    "    let state = null, key = '', connectionCode = '', busy = false;",
    "    const $ = id => document.getElementById(id);",
    "    function validEndpoint(value) { return /^https:\\/\\/script\\.google\\.com\\/macros\\/s\\/[A-Za-z0-9_-]{16,200}\\/exec$/.test(value); }",
    "    function selectedEndpoint() { return $('endpoint').value.trim(); }",
    "    function message(text, error = false) { $('status').textContent = text; $('status').className = error ? 'error' : ''; }",
    "    function render() {",
    "      const endpoint = selectedEndpoint();",
    "      const usable = state && state.ok;",
    "      $('deploymentGuide').hidden = !!(usable && validEndpoint(endpoint));",
    "      $('prepare').disabled = busy || !usable || state.configured || !validEndpoint(endpoint);",
    "      $('refresh').disabled = busy;",
    "      $('rotate').hidden = !usable || !state.configured;",
    "      $('rotate').disabled = busy || !usable || !validEndpoint(endpoint);",
    "      $('endpoint').disabled = busy;",
    "      connectionCode = '';",
    "      if (usable && key && state.keyMatches && validEndpoint(endpoint)) {",
    "        const bytes = new TextEncoder().encode(JSON.stringify({ version: 1, endpoint, key, datasetId: state.datasetId }));",
    "        connectionCode = 'MYTODO2:' + btoa(String.fromCharCode(...bytes)).replace(/=/g, '').replace(/\\+/g, '-').replace(/\\//g, '_');",
    "      }",
    "      $('ready').hidden = !connectionCode;",
    "      $('connection').value = connectionCode;",
    "      $('copy').disabled = $('download').disabled = busy || !connectionCode;",
    "    }",
    "    function endpointChanged() {",
    "      render();",
    "      const endpoint = selectedEndpoint();",
    "      if (!validEndpoint(endpoint)) message(endpoint ? '웹 앱 URL 전체를 붙여넣으세요. 배포 ID 또는 /dev 주소는 사용할 수 없습니다.' : '배포 화면의 웹 앱 URL (/exec)을 붙여넣으세요.', !!endpoint);",
    "      else if (connectionCode) message('연결 정보를 복사해 위젯에 붙여넣으세요.');",
    "      else if (state && state.configured) message('이미 연결된 시트입니다. 보관한 연결 정보를 사용하세요.');",
    "      else message('URL 형식이 올바릅니다. 전용 코드 만들기를 누르세요. 실제 연결은 위젯에서 확인합니다.');",
    "    }",
    "    function accept(data) {",
    "      busy = false;",
    "      if (!data || !data.ok) { message(data && data.message || '상태를 확인하지 못했습니다. 다시 시도하세요.', true); render(); return; }",
    "      state = data;",
    "      if (!data.ok) return;",
    "      if (key && !data.keyMatches) { key = ''; message('키가 변경되었거나 폐기되었습니다. 보관한 연결 정보를 확인하세요.', true); }",
    "      else if (key) message('연결 정보를 만들었습니다. 복사하고 다른 PC 연결용으로 보관하세요.');",
    "      else if (data.configured) message('이미 연결된 시트입니다. 다른 PC에는 보관한 연결 정보를 사용하세요. 키를 잃었다면 새 키로 재설정합니다.');",
    "      else if (!validEndpoint(selectedEndpoint())) message('배포 화면의 웹 앱 URL (/exec)을 위 입력란에 붙여넣으세요.');",
    "      else message('URL 형식이 올바릅니다. 전용 코드 만들기를 누르세요. 실제 연결은 위젯에서 확인합니다.');",
    "      render();",
    "    }",
    "    function failure() { busy = false; message('Google에서 실행하지 못했습니다. 본인 계정·권한·인터넷 연결을 확인한 뒤 다시 시도하세요.', true); render(); }",
    "    function refresh() {",
    "      if (busy) return; busy = true; render(); message('설치한 시트의 연결 상태를 확인합니다…');",
    "      google.script.run.withSuccessHandler(accept).withFailureHandler(failure).getMyToDoSetupState(key);",
    "    }",
    "    function newKey() {",
    "      if (!globalThis.crypto || !globalThis.crypto.getRandomValues) throw new Error('SECURE_RANDOM_UNAVAILABLE');",
    "      const bytes = crypto.getRandomValues(new Uint8Array(32));",
    "      return btoa(String.fromCharCode(...bytes)).replace(/=/g, '').replace(/\\+/g, '-').replace(/\\//g, '_');",
    "    }",
    "    function prepare(rotate) {",
    "      if (busy || !state || !state.ok || !validEndpoint(selectedEndpoint())) return;",
    "      if (rotate && !confirm('기존 PC의 연결이 중단됩니다. 모든 PC에 새 연결 정보를 입력해야 합니다. 새 키로 재설정할까요?')) return;",
    "      try { if (rotate || !key) key = newKey(); }",
    "      catch (_) { message('안전한 키를 생성하지 못했습니다. 최신 브라우저에서 다시 열어 주세요.', true); return; }",
    "      state.keyMatches = false;",
    "      busy = true; render(); message('내 시트의 연결을 준비합니다…');",
    "      google.script.run.withSuccessHandler(accept).withFailureHandler(failure).initializeMyToDoCopy(key, rotate);",
    "    }",
    "    async function copyConnection() {",
    "      if (!connectionCode || busy) return;",
    "      try {",
    "        if (!navigator.clipboard || !navigator.clipboard.writeText) throw new Error('FALLBACK');",
    "        await navigator.clipboard.writeText(connectionCode);",
    "      } catch (_) {",
    "        let area, copied = false;",
    "        try {",
    "          area = document.createElement('textarea'); area.value = connectionCode;",
    "          area.style.position = 'fixed'; area.style.left = '-10000px'; document.body.appendChild(area);",
    "          area.focus(); area.select(); copied = document.execCommand('copy');",
    "        } catch (_) { copied = false; }",
    "        finally { if (area) area.remove(); }",
    "        if (!copied) { message('브라우저에서 복사를 허용하지 않았습니다. 코드 파일 보관을 사용하세요.', true); return; }",
    "      }",
    "      message('복사했습니다. 위젯에 붙여넣고 저장하세요. 다른 PC용으로 연결 정보 파일도 보관할 수 있습니다.');",
    "    }",
    "    function downloadConnection() {",
    "      if (!connectionCode || busy) return;",
    "      const url = URL.createObjectURL(new Blob([connectionCode + '\\n'], { type: 'text/plain;charset=utf-8' }));",
    "      const anchor = document.createElement('a'); anchor.href = url; anchor.download = 'MyToDo2.mytodo-connection';",
    "      document.body.appendChild(anchor); anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);",
    "      message('연결 정보 파일을 보관했습니다. 파일을 가진 사람은 메모를 읽고 수정할 수 있습니다.');",
    "    }",
    "    refresh();",
    "  </script>",
    "</body>",
    "</html>",
  ].join("\n");
}
