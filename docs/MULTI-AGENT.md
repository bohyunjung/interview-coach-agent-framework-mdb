# 에이전트 모드

`apphost.settings.json`의 `AgentMode`로 모드를 정합니다. AppHost에 `--mode` 인자를 넘겨도 됩니다.

## HandOff (기본)
역할이 다른 에이전트 여러 개가 같은 Copilot 모델을 쓰고, 하나가 끝나면 다음 에이전트에게 대화를 넘겨 받습니다. 처음 요청은 `triage`가 받아 어느 에이전트에게 보낼지 정합니다.
- `triage`: 요청을 분류합니다. 지난 면접에 대한 질문은 `memory_coach`로 보냅니다.
- `receptionist`: 이력서와 JD를 MarkItDown으로 읽고, 회사명과 직무로 세션을 만듭니다.
- `behavioural_interviewer`, `technical_interviewer`: 면접 질문을 하고, 대화록을 세션에 기록합니다.
- `summariser`: 면접이 끝나면 세션을 완료 처리하고, 피드백 항목과 사용자 프로필을 저장합니다.
- `memory_coach`: 지난 면접에 관한 질문에 답합니다. Recall 도구로 면접 목록, 회사별 검색, 최근 피드백, 의미 검색, 프로필을 조회하고, 답한 뒤에는 `triage`로 돌아갑니다.

## Single
에이전트 하나가 처음부터 끝까지 맡습니다. Session, Recall, Consolidate 도구와 MarkItDown을 모두 씁니다.

두 모드 모두 Short Term 미들웨어가 에이전트를 감싸 대화를 저장합니다. 에이전트별 도구 구성은 `AgentDelegateFactory.cs`와 `MemoryToolSets.cs`에, 메모리 설계는 [MEMORY.md](MEMORY.md)에 있습니다.
