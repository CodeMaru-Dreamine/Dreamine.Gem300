# Dreamine.Gem300

Dreamine.Gem300은 독립적으로 테스트 가능한 GEM300 장비 도메인 모듈의 1차
구현입니다.

[➡️ English Version](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/README.md)

## 구현한 도메인 경계

- E39 방식 객체 키, 필수 `ObjType`/`ObjID`, 형식화된 RO/RW 속성 및 취소 가능한
  Object Action
- E87 Load Port Transfer, Access Mode, Reservation, Association, Carrier ID,
  Slot Map 및 Carrier Access 상태
- E90 Substrate Transport, Processing, ID Confirmation, 위치 점유 및 체류 이력
- E40 Process Job 생명주기, Recipe/Material 존재 확인, Pause, Stop, Abort,
  Complete 및 Delete
- E94 직렬 Control Job Queue, 순서화된 Process Job 단독 소유, Select, 수동 Start,
  Pause, Complete, Abort 및 Delete
- 주입 시간과 제한 용량을 사용하는 도메인 이벤트 저널
- 결정적 Abort 정리와 Cancellation 전파를 제공하는 Experimental
  Carrier→Process→반출 Workflow

각 기능 모듈은 `Dreamine.Gem300.Abstractions`의 분리된 인터페이스로 노출되며,
서로의 내부 상태를 직접 변경하지 않습니다.

## 표준과 제한

이번 구현의 로컬 Normative 근거는 E39-0703(Reapproved 1109), E40-0312,
E87-0312, E90-0312, E94-0314입니다. [요구사항 추적표](./docs/SEMI_REQUIREMENTS_TRACE.md)에
최신 Revision을 별도로 기록했으므로 현재판 적합성·인증·벤더 상호운용성을 주장하지
않습니다.

E39.1, E40.1, E87.1, E90.1, E94.1 원문은 로컬에 없었습니다. 따라서 표준
SECS-II wire mapping, ACK 숫자 및 서비스 오류 코드를 추측하지 않습니다.
E116/E116.1 원문도 없어 Equipment Performance Tracking은 근거 없는 공개 API를
만드는 대신 명시적으로 Blocked 처리했습니다. E84 Handoff, 영속 복구 및 연결 상태
재동기화는 이번 1차 범위 밖입니다.

## 조립 예시

```csharp
var gem300 = new Gem300Runtime(gemRuntime, gemRuntime.ProcessPrograms);
gem300.Carriers.RegisterLoadPort("PORT-1");
gem300.Carriers.SetInService("PORT-1");
```

예시의 `gemRuntime`은 `Dreamine.Gem.GemRuntime`을 가정하지만, Abstractions는
공급자 중립 계약에만 의존합니다.

## 라이선스

MIT.
