# 메모리 설계

아래에서 메모리마다 무엇을 저장하고, 왜 그렇게 설계했고, 어떻게 꺼내는지 설명합니다.

피드백 항목의 임베딩은 Voyage AI의 `voyage-4-lite` 모델(1024차원, cosine 유사도)로 만듭니다. 앱과 샘플 데이터 스크립트가 직접 Voyage API를 호출해 벡터를 만들고, 문서에 함께 저장합니다.

컬렉션은 MongoDB C# 드라이버(`MongoDB.Driver`)로 접근합니다. 구현은 `src/InterviewCoach.Memory/`(`MongoMemory.cs`, `Repositories.cs`, `Models.cs`, `VoyageEmbeddingService.cs`)에 있습니다.

아래 JSON은 실제 저장 모델에 맞춘 가상 예시입니다. 날짜와 ObjectId는 MongoDB Extended JSON으로 표시합니다. 네 예시는 같은 면접을 가리키며, `sessions._id`가 대화의 `conversationId`, 피드백의 `sessionId`, 프로필의 근거 세션 ID로 연결됩니다.

## Short Term (단기): 대화 (`chat_messages`)
- 저장: 사용자와 에이전트의 발화를 한 턴에 문서 하나씩 저장합니다. 필드는 역할, 텍스트, 발화한 에이전트, 대화 ID, 시각입니다.
- 이유: 대화 저장을 LLM이 호출하는 도구에 맡기면 빠뜨릴 수 있습니다. 그래서 `ShortTermMemory` 미들웨어가 에이전트를 감싸고, 모든 턴을 자동으로 저장합니다. 여러 에이전트가 차례로 답한 경우에는 답한 에이전트별로 나눠 저장합니다. 에이전트 이름(`AuthorName`)이 비어 있으면 `AgentId`를 대신 씁니다.
- 조회: 면접 SessionId인 `conversationId`로 조회합니다. Agent의 `GET /chat/{sessionId}/messages`가 턴을 시간순으로 돌려주고(여러 에이전트가 이어서 한 답변은 하나로 합칩니다), `/chat/{sessionId}` 페이지가 이를 읽어 새로고침, 새 탭, 재시작 뒤에도 대화를 복원합니다. 오래된 턴은 TTL 인덱스(`create_indexes.py`가 생성, 기본 30일)가 지웁니다.

예시 문서:

```json
{
  "_id": { "$oid": "68c8ed000000000000000001" },
  "conversationId": "a1000000-0000-4000-8000-000000000004",
  "role": "assistant",
  "authorName": "interviewer",
  "text": "How would you investigate a sudden increase in queue latency?",
  "createdAt": { "$date": "2026-09-16T05:35:00Z" }
}
```

`_id`는 ObjectId입니다. `authorName`은 발화한 에이전트 이름이며, 사용자 발화에서는 `null`일 수 있습니다. `createdAt`은 시간순 조회와 TTL 만료의 기준입니다.

## Episodic (일화): 면접 세션 (`sessions`)
- 저장: 면접 한 건당 문서 하나입니다. 회사, 직무, 이력서와 JD 텍스트, 대화록, 요약 피드백, 완료 시각을 담습니다.
- 이유: "무슨 일이 언제 있었나"는 문서 하나로 두는 것이 자연스럽고, 면접마다 담기는 내용이 달라도 구조를 바꾸지 않고 저장할 수 있습니다.
- 조회: `list_recent_interviews`, `find_interviews_by_company`(대소문자를 무시하는 정규식), `get_interview_session` 도구를 씁니다.

예시 문서:

```json
{
  "_id": "a1000000-0000-4000-8000-000000000004",
  "companyName": "Northgate Cloud",
  "roleTitle": "Backend Platform Engineer",
  "resumeLink": null,
  "resumeText": "Backend engineer with experience operating distributed job queues.",
  "proceedWithoutResume": false,
  "jobDescriptionLink": null,
  "jobDescriptionText": "Build reliable backend platforms and improve observability.",
  "proceedWithoutJobDescription": false,
  "transcript": "Interviewer: How would you investigate queue latency?\nCandidate: I would start with metrics and traces.",
  "feedback": "Strong observability-driven debugging. Quantify throughput and retry limits.",
  "isCompleted": true,
  "createdAt": { "$date": "2026-09-16T05:30:00Z" },
  "updatedAt": { "$date": "2026-09-16T06:12:00Z" },
  "completedAt": { "$date": "2026-09-16T06:12:00Z" }
}
```

`_id`는 문자열 세션 ID입니다. 이력서, JD, 대화록은 짧게 작성했습니다. 진행 중인 면접은 `isCompleted`가 `false`이고 `completedAt`은 `null`입니다.

## Semantic (의미): 피드백 항목 (`feedback_items`)
- 저장: 세션당 문서 N개를 저장합니다. 피드백 항목 하나가 문서 하나이고, 필드는 `kind`(strength | improvement), `topic`, `text`, 회사, 직무, 날짜, `embedding`입니다.
- 이유: 항목 단위로 나누면 종류(강점/개선점)로 거르고, 의미가 가까운 순서로 정렬하고, 어느 회사 면접에서 나온 말인지 함께 보여 줄 수 있습니다.
- 조회:
  - `search_feedback`: MongoDB Vector Search on Atlas의 `$vectorSearch`(인덱스 `feedback_vector_index`, `kind` 필터)로 "내 X를 칭찬한 회사가 어디지?" 같은 질문에 답합니다.
  - `get_recent_feedback_items`: 최근 완료된 세션 N개의 항목을 가져오고, LLM이 공통 주제로 묶습니다.

예시 문서:

```json
{
  "_id": "fi-a1000000-0000-4000-8000-000000000004-1",
  "sessionId": "a1000000-0000-4000-8000-000000000004",
  "companyName": "Northgate Cloud",
  "roleTitle": "Backend Platform Engineer",
  "kind": "strength",
  "topic": "observability-driven debugging",
  "text": "Used metrics and traces to identify the failing service.",
  "textHash": "b58949c4057ceacfcf44fc289749352df12dc62e1fe9d53399e1cf64bdcb3274",
  "embeddingModel": "voyage-4-lite",
  "embedding": [0.012, -0.034, 0.056],
  "createdAt": { "$date": "2026-09-16T06:12:00Z" }
}
```

`embedding`은 구조를 보여 주기 위한 가상 값 3개만 표시했습니다. 실제 저장 벡터는 1024차원이어야 합니다. `textHash`는 `text`의 SHA-256 해시이며, `kind`는 `strength` 또는 `improvement`입니다. 회사와 직무를 함께 저장하므로 검색 결과에서 세션을 다시 조회하지 않고 출처를 표시할 수 있습니다.

## Semantic (의미): 사용자 프로필 (`user_profile`)
- 저장: 문서 하나(`_id: "default"`)를 저장합니다. 요약, 목표 직무, 강점, 개선점, 면접 횟수를 담고, 개선점에는 `recurring` 같은 상태와 근거가 된 세션 ID가 붙습니다.
- 이유: 면접이 끝날 때마다 내용을 종합해 두면 다음 코칭이 이미 아는 내용에서 출발할 수 있습니다.
- 조회: `get_user_profile`로 읽습니다. `summariser`가 `save_session_feedback` 다음에 `save_user_profile`로 다시 씁니다.

예시 문서:

```json
{
  "_id": "default",
  "displayName": "Demo Candidate",
  "targetRoles": ["Backend Platform Engineer"],
  "interviewCount": 1,
  "lastSessionId": "a1000000-0000-4000-8000-000000000004",
  "summary": "Strong at debugging with observability data; needs more specific scale targets.",
  "strengths": [
    {
      "topic": "observability-driven debugging",
      "status": null,
      "sessionIds": ["a1000000-0000-4000-8000-000000000004"]
    }
  ],
  "growthAreas": [
    {
      "topic": "give numeric targets",
      "status": "recurring",
      "sessionIds": ["a1000000-0000-4000-8000-000000000004"]
    }
  ],
  "updatedAt": { "$date": "2026-09-16T06:13:00Z" }
}
```

`_id`는 항상 `"default"`이며, `interviewCount`는 완료된 면접 수입니다. 각 주제의 `sessionIds`는 근거가 된 면접을 가리킵니다. `status`는 개선점에만 쓰며 `recurring`, `improving`, `resolved`로 상태를 나타냅니다. 이 예시는 면접 한 건을 종합했지만, 이후 면접이 끝나면 같은 프로필에 여러 세션의 근거가 반영됩니다.

## 에이전트별로 쓰는 도구
Memory MCP Server의 도구를 역할에 따라 세 묶음으로 나눠, 에이전트마다 필요한 것만 쓰게 합니다(`MemoryToolSets.cs`).

| 묶음 | 도구 | 쓰는 에이전트 |
|---|---|---|
| Session | `add_interview_session`, `get_interview_session`, `update_interview_session`, `complete_interview_session` | `receptionist`, 면접관 에이전트, `summariser` |
| Recall | `list_recent_interviews`, `find_interviews_by_company`, `get_recent_feedback_items`, `search_feedback`, `get_user_profile` | `memory_coach` |
| Consolidate | `save_session_feedback`, `get_user_profile`, `save_user_profile` | `summariser` |

면접이 끝나면 `summariser`가 다음 순서로 저장합니다.
1. `update_interview_session`과 `complete_interview_session`으로 세션을 마무리합니다.
2. `save_session_feedback`으로 피드백을 3~8개 항목으로 나눠 저장합니다.
3. `get_user_profile`로 기존 프로필을 읽고, 이번 면접 내용을 반영해 `save_user_profile`로 다시 저장합니다.

## 데이터 들여다보기
`.vscode/mcp.json`에 공식 MongoDB MCP Server가 읽기 전용으로 등록돼 있어서, Copilot Chat에서 에이전트가 저장한 데이터를 조회할 수 있습니다. 설정 방법은 [설정 및 실행 가이드](SETUP.md#6-선택-copilot-chat에서-메모리-들여다보기), 질문 예시는 [시나리오 4](DEMO.md#시나리오-4-읽기-전용-mongodb-mcp-server로-데이터-들여다보기)에 있습니다.
