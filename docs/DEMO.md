# 데모 시나리오

Atlas에 샘플 데이터가 들어 있고 앱이 실행 중이라고 가정합니다([README](../README.md)의 빠른 시작을 마친 상태). 샘플 데이터는 가상 인물의 지난 면접 6건(Harborline Payments, Kestrel Commerce, Lumenfield AI, Northgate Cloud, Tessera Health, Quillstream)입니다. 피드백에는 일부러 반복되는 주제를 넣었습니다.

- "결과를 수치로 말하기"는 6건 중 5건에서 개선점으로 나옵니다.
- "트레이드오프 설명"은 처음에는 개선점이었다가 마지막 면접에서 강점이 됩니다.
- "실패/재시도 처리"는 계속 강점입니다.

시나리오마다 쓰는 메모리를 함께 적었습니다. 메모리 설계는 [MEMORY.md](MEMORY.md)를 보세요.

## 0. 준비 (시연 전)
1. `create_indexes.py`를 실행하고 `seed_memory.py --reset`으로 샘플 데이터만 남깁니다. Atlas UI의 Search & Vector Search에서 `feedback_vector_index`의 상태가 `READY`인지 확인합니다.
2. Aspire 대시보드에서 `webui`, `agent`, `mcp-memory`, `mcp-markitdown`이 모두 Running인지 확인합니다.
3. MongoDB for VS Code Extension에서 Atlas에 연결하고 내장 MCP Server를 시작합니다([시나리오 4](#시나리오-4-읽기-전용-mongodb-mcp-server로-데이터-들여다보기) 참고). Atlas UI의 Data Explorer도 탭으로 열어 둡니다.

## 시나리오 1. 지난 면접 회상 (샘플 데이터만 사용)
새 채팅에서 바로 질문합니다. `triage`가 `memory_coach`에게 넘기고, 어떤 도구가 호출됐는지는 Aspire 대시보드나 로그에서 보여 줍니다.

| 프롬프트 | 기대 동작 | 쓰는 메모리와 검색 방식 |
|---|---|---|
| `Kestrel 면접이 언제였지? 어떤 포지션이었어?` | 회사명으로 세션을 찾아 날짜와 직무를 답합니다. | Episodic (일화), 메타데이터 필터 (`find_interviews_by_company`) |
| `Lumenfield AI 면접 피드백 요약해줘.` | 그 세션의 강점과 개선점을 요약합니다. | Episodic (일화) + Semantic (의미): 피드백 항목 |
| `내 최근 5건 면접에서 공통으로 나온 피드백이 뭐야?` | 최근 5건의 항목을 가져와 LLM이 주제별로 묶고, "수치로 말하기"가 반복된다고 짚습니다. | Semantic (의미): 피드백 항목, 최근 N건 조회 (`get_recent_feedback_items`) |
| `내 디버깅 역량을 칭찬한 회사가 어디였지?` | 표현이 달라도 "observability-driven debugging"(Northgate Cloud)을 찾습니다. | Semantic (의미): 피드백 항목, 벡터 검색 (`search_feedback`, 강점 필터) |
| `내가 계속 약한 부분이 뭐야? 그게 나아지고 있어?` | 프로필에서 반복되는 개선점과 나아진 항목(트레이드오프)을 비교합니다. | Semantic (의미): 사용자 프로필 (`get_user_profile`) |

회사와 날짜는 정확히 일치하는 값으로 거르고, "칭찬한 회사"는 의미로 검색하고, "공통 피드백"은 최근 항목을 가져와 LLM이 요약합니다. 질문 유형마다 검색 방식이 다르다는 점을 짚어 주세요.

## 시나리오 2. 새 면접 진행 (Short Term 저장과 Episodic 쌓기)
1. 채팅에 `samples/`의 이력서(`resume-peter-parker.pdf`)를 첨부해 보냅니다. 첨부는 메시지당 1개입니다. 예: `이 이력서로 모의 면접을 준비할게요. 회사는 Orbitwave Labs입니다.` 이어서 직무기술서(`jd-cloud-solution-architect.pdf`)를 첨부해 보냅니다. 예: `이게 JD예요. 포지션은 Cloud Solution Architect입니다.`
   - JD와 다른 포지션명을 말하면 에이전트가 어느 쪽으로 할지 되묻습니다. 샘플 JD의 직무로 말하면 흐름이 끊기지 않습니다.
   - `receptionist`가 세션을 만들면서 회사명과 직무를 저장합니다. (Episodic)
2. 면접관의 질문에 두세 번 답합니다. 일부러 숫자 없이 답해 보세요. 예: `캐시를 도입해서 성능을 많이 개선했어요.`
3. Short Term (단기) 저장을 보여 줍니다. 주소창의 `/chat/<세션 ID>`를 짚어 주고, 면접 도중에 새로고침합니다. 대화가 그대로 복원됩니다. 같은 주소를 새 탭에서 열거나, `New chat`을 누른 뒤 브라우저의 뒤로 가기를 눌러도 이어집니다. 복원되는 대화는 서버 메모리가 아니라 MongoDB의 `chat_messages`에서 읽어 오기 때문에, WebUI를 재시작해도 남아 있습니다.
   - Atlas UI에서 `chat_messages`를 열어 사용자와 에이전트의 발화가 턴 단위로 쌓인 것을 보여 줍니다. 저장은 LLM이 아니라 미들웨어가 자동으로 합니다. 여러 에이전트(`triage`, `receptionist` 등)가 이어서 답하면 발화한 에이전트별로(`authorName`) 나뉘어 저장되고, 화면에는 하나의 답변으로 합쳐 보입니다.
4. 면접을 마칩니다. 예: `여기까지 할게요. 면접 마무리하고 피드백 부탁해요.`
   - `summariser`가 세션을 완료 처리하고, 피드백을 3~8개 항목으로 나눠 임베딩과 함께 저장하고, 프로필을 갱신합니다.

저장된 내용은 Atlas UI 또는 MongoDB MCP Server로 확인합니다.
- `sessions`의 Orbitwave Labs 문서에서 `transcript`, `feedback`, `isCompleted`
- `feedback_items`의 새 항목에서 `kind`, `topic`, `embedding`
- `user_profile`의 `interviewCount`가 6에서 7로 증가

## 시나리오 3. 방금 저장한 내용 바로 검색
같은 채팅이나 새 채팅에서 묻습니다.

| 프롬프트 | 기대 동작 |
|---|---|
| `Orbitwave Labs 면접은 어땠어?` | 방금 저장한 세션을 회상합니다. |
| `최근 면접들에서 숫자로 말하라는 피드백이 반복돼?` | 이번 면접을 포함한 7건 중 반복되는 개선점으로 확인합니다. 프로필의 `recurring` 항목과 일치합니다. |
| `다음 면접 전에 가장 먼저 고쳐야 할 게 뭐야?` | 프로필과 최근 피드백을 종합해 코칭합니다. |

이 시나리오에서는 대화(Short Term)가 세션과 피드백 항목(Episodic, Semantic)으로 정리되고, 다시 프로필(Semantic)로 종합되는 흐름을 볼 수 있습니다.

## 시나리오 4. 읽기 전용 MongoDB MCP Server로 데이터 들여다보기
앱과는 별개의 경로로, **MongoDB for VS Code Extension의 내장 MCP Server**를 사용합니다. MongoDB 사이드바에서 Atlas 연결을 추가하고 접속한 다음, 명령 팔레트의 **MongoDB: Start MCP Server**를 실행합니다. Copilot Chat을 Agent 모드로 열고 도구 선택기에서 Extension이 제공하는 MongoDB MCP 도구를 활성화합니다. [.vscode/settings.json](../.vscode/settings.json)의 `mdb.mcp.readOnly: true` 설정으로 MCP 쓰기 도구는 비활성화돼 있습니다. 자세한 연결 절차는 [설정 및 실행 가이드](SETUP.md#6-선택-copilot-chat에서-메모리-들여다보기)에 있습니다.

참가자가 직접 해 볼 수 있는 질문입니다. 위쪽일수록 쉽습니다.

| 질문 | 보여 주는 것 |
|---|---|
| `interview_coach 데이터베이스의 컬렉션별 문서 수를 알려줘.` | 컬렉션 4개와 문서 수 |
| `sessions에서 완료된 면접을 날짜순으로 회사명과 직무만 보여줘.` | Episodic (일화) 조회 |
| `feedback_items에서 kind가 strength인 항목의 topic을 빈도순으로 보여줘.` | 집계 파이프라인 |
| `feedback_items에서 개선점(improvement)의 topic별 개수를 세고, 어느 회사에서 나왔는지도 같이 보여줘.` | 반복되는 개선점이 데이터에서 실제로 보입니다. |
| `Northgate Cloud 세션의 요약 피드백과 그 세션의 feedback_items를 함께 보여줘.` | `$lookup`으로 두 컬렉션을 연결 |
| `feedback_items 문서 하나를 embedding 필드는 빼고 보여주고, embedding 배열의 길이도 알려줘.` | 임베딩이 문서 안에 함께 저장됨 (1024차원) |
| `feedback_items의 인덱스와 feedback_vector_index 설정을 설명해줘.` | 벡터 인덱스 정의 (`embedding`, cosine, `kind` 필터) |
| `chat_messages의 인덱스를 설명해줘. 오래된 대화는 어떻게 지워져?` | TTL 인덱스 |
| `chat_messages에서 가장 최근 conversationId의 발화를 시간순으로 보여줘.` | 시나리오 2에서 쌓인 턴과 `authorName` |
| `user_profile 문서를 보여주고, 반복되는 개선점이 몇 개 세션에서 나왔는지 설명해줘.` | 프로필에 종합된 결과 |
| `sessions에서 Orbitwave Labs 세션을 삭제해줘.` | 읽기 전용이라 거부됩니다. |

같은 데이터를 MongoDB 사이드바에서 열어 GUI와 Copilot의 조회 결과를 비교할 수 있습니다. Extension의 GUI와 내장 MCP Server는 같은 활성 연결을 사용합니다. 클러스터를 바꾸면 Copilot의 조회 대상도 바뀌므로 시연 전에 연결을 확인하세요. MCP의 읽기 전용 설정이 GUI 작업까지 제한하지는 않습니다.

## 질문이 나올 때
- Voyage 키가 없거나 잘못돼도 의미 검색만 실패하고 채팅은 계속됩니다. Short Term 저장이 실패해도 로그만 남기고 대화는 막지 않습니다.
- `chat_messages`에는 TTL 인덱스(기본 30일)가 있어서 오래된 대화가 자동으로 삭제됩니다.
- 데모 데이터를 되돌리려면 `python scripts/memory/seed_memory.py --reset`을 실행합니다. 시나리오 2에서 만든 세션, 피드백 항목, 대화 기록이 지워지고 샘플 데이터만 남습니다. 프로필의 `interviewCount`도 6으로 돌아갑니다.

## 진행 팁
- 시나리오 1은 질문이 짧아서 라이브로 해도 부담이 적습니다. 시나리오 2는 모델 응답이 오래 걸리니, 청중 앞에서는 답변을 미리 준비해 두고 붙여 넣는 방법을 권합니다.
- 시나리오 2를 라이브로 하기 부담스러우면 미리 한 번 실행해 두고, 시나리오 3과 4만 라이브로 진행해도 됩니다.
- 모델의 응답에 따라 호출되는 도구가 달라질 수 있으니, 리허설에서 질문을 한 번씩 실행해 보세요.
- 시나리오 2에서 에이전트가 같은 면접 세션을 두 번 만들 때가 있습니다. 시나리오 4의 `sessions` 질문에서 중복이 보이면 `--reset`으로 정리하세요.
