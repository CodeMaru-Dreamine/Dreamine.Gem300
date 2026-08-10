# GEM300 요구사항 추적표

기준일: 2026-08-10

이 문서는 저장소 밖의 로컬 표준 원문을 읽기 전용으로 대조한 결과와 1차 구현
경계를 기록한다. 표준 원문, 고객사명, 내부 사양 및 장문 인용은 포함하지 않는다.

증거 수준:

- `Normative`: 보유한 SEMI 원문에서 확인
- `Official Public`: SEMI 공식 Store의 공개 Revision·설명에서 확인
- `Experimental`: 규범 개념을 조합한 자체 통합
- `Blocked`: 필요한 규범 원문이 없어 구현 보류

## Revision 기준

| 표준 | 로컬 원문 | 공식 Store 대조 | 이번 적용 |
|---|---|---|---|
| SEMI E39 | E39-0703 (Reapproved 1109), 42쪽 | E39-1218 (Reapproved 0124) Current | 객체 식별·속성·기본 서비스의 도메인 경계 |
| SEMI E39.1 | 없음 | E39.1이 E39 제품군에 포함됨 | Blocked — wire mapping 원문 필요 |
| SEMI E40 | E40-0312, 36쪽 | E40-0226 Current | Process Job 상태·생명주기 |
| SEMI E40.1 | 없음 | E40.1-0226 | Blocked — SECS-II mapping 원문 필요 |
| SEMI E87 | E87-0312, 90쪽 | E87-0726 Current | Load Port·Carrier 상태·검증·Slot Map |
| SEMI E87.1 | 없음 | E87.1-0726 | Blocked — SECS-II mapping 원문 필요 |
| SEMI E90 | E90-0312, 44쪽 | E90-1125 Current | Substrate·Location·이력·처리 상태 |
| SEMI E90.1 | 없음 | E90.1-1125 | Blocked — SECS-II mapping 원문 필요 |
| SEMI E94 | E94-0314, 39쪽 | E94-0226 Current | Control Job·Queue·Process Job 순서 |
| SEMI E94.1 | 없음 | E94.1-0226 | Blocked — SECS-II mapping 원문 필요 |
| SEMI E116 | 없음 | E116-0324 Current | Blocked — 상태·속성·계산 규범 원문 필요 |
| SEMI E116.1 | 없음 | E116.1-0623 (Reapproved 0324) 포함 | Blocked — SECS-II mapping 원문 필요 |
| SEMI E84 | E84-0701 | 최신판 재대조 필요 | 외부 Carrier Handoff 연계; 이번 구현 제외 |

로컬판과 현재판 사이의 변경점은 최신 원문을 확보하기 전까지 확인되지 않았다.
따라서 아래 구현은 로컬 Revision 기반이며 현재판 적합성을 주장하지 않는다.

## 원문 대조 결과

| 모듈 | 로컬 근거 | 확인한 핵심 | 구현 경계 |
|---|---|---|---|
| Object Services | E39 §4, §8, §9–§13 | ObjType/ObjID, 공개 속성, RO/RW, Get/Set/Action 개념 | 강타입 객체 키와 `SecsItem` 속성 저장소 |
| Process Job | E40 §8.3, Figure 4, Table 1, §9 | QUEUED/POOLED부터 ACTIVE·POST ACTIVE 하위 상태, 생성·삭제 | 전체 1차 상태 전이와 불변 스냅샷 |
| Load Port | E87 §9, §11–§13 | Transfer, Access Mode, Reservation, Association 독립 상태 | 포트별 직렬화된 상태 모델 |
| Carrier | E87 §10.2–§10.7, Figure 2, Table 7 | ID/Slot Map/Accessing 병렬 상태, 객체 수명 | Carrier Aggregate와 검증·접근·반출 |
| Substrate | E90 §8–§13, Figure 3 | Transport/Processing/Reading 병렬 상태, 위치·이력 | Substrate Aggregate와 위치 점유 일관성 |
| Control Job | E94 §8–§10, Figure 2, Table 3 | QUEUED, SELECTED, WAITING, EXECUTING, PAUSED, COMPLETED | 순서화된 Process Job 연결과 상태 모델 |
| Equipment Performance | 공식 공개 설명만 확인 | 상태·속성·시간 계산의 정확한 규범 부족 | Blocked — 공개 계약도 확정하지 않음 |

## 요구사항과 테스트 추적

| 요구사항 | 관련 API/구현 | 예정 테스트 | 상태 |
|---|---|---|---|
| ObjType 내 ObjID 고유성 | `Gem300ObjectService` | 중복/RO/RW/Action/timeout/동시 변경 | 도메인 구현·테스트; wire 제외 |
| Load Port 독립 상태 | `CarrierManager` | 다중 포트/예약/접근 모드/잘못된 전이 | 구현·테스트 |
| Carrier ID·Slot Map 검증 | `CarrierManager` | accept/reject/크기/병렬 상태 | 구현·테스트 |
| Carrier 접근·완료·반출 | `CarrierManager` | 정상/stop/reject/removal/전송 방향 | 구현·테스트 |
| Substrate 위치 점유·이력 | `SubstrateTracker` | 이동/중복 위치/주입 시간/lost | 구현·테스트 |
| Substrate 처리 상태 | `SubstrateTracker` | complete/abort/stop/reject/skip | 구현·테스트 |
| Process Job 생명주기 | `ProcessJobManager` | auto/manual/pause/stop/abort/delete/Recipe·Material | 구현·테스트 |
| Control Job 생명주기 | `ControlJobManager` | queue/select/start/pause/순서/단독 소유/delete | 구현·테스트 |
| 기능 간 이벤트 순서 | `IGem300EventJournal` | 단조 Sequence/주입 시간/용량 | 자체 관찰 경계 구현·테스트 |
| Carrier→Job→반출 조정 | `Gem300WorkflowCoordinator` | 정상/검증 실패/처리 예외/취소/Abort 정리 | Experimental 구현·테스트 |
| 표준 SECS-II 서비스 | 없음 | 없음 | Blocked — `.1` 원문 필요 |
| Equipment Performance | 없음 | 없음 | Blocked — E116/E116.1 원문 필요 |

## 명시적 제한

- 상태 숫자, ACK, 서비스 메시지 구조를 추측하지 않는다.
- 도메인 API 이름은 자체 구현 경계이며 표준 wire 서비스 이름의 완전한 구현을
  뜻하지 않는다.
- E84 handoff, 영속 복구, 연결 재동기화 및 장비별 Recipe/Material 정책은 이번
  1차 범위 밖이다.
- 고객·사내 자료는 테스트 아이디어에만 사용하며 Normative 근거로 사용하지 않았다.
- 내부 통합 테스트는 실제 장비·독립 Simulator·인증 시험을 대체하지 않는다.

## 1차 구현 검증 결과

- `Dreamine.Gem300.Abstractions.Tests`: 9개 통과
- `Dreamine.Gem300.Tests`: 33개 통과
- Release 빌드: 감사 완료 시점의 결과를 최종 검증 절에 기록
- 표준 `.1` wire 통합 및 독립 Simulator 시험: 수행하지 않음
