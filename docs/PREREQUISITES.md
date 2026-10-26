# 준비 사항

앱을 실행하기 전에 아래 계정과 도구를 준비하세요. 계정과 키 생성, 저장소 설정, 앱 실행 절차는 [설정 및 실행 가이드](SETUP.md)를 참고하세요.

## 필수

- MongoDB Atlas 계정 및 프로젝트. 앱 메모리는 Atlas에 저장하며 무료 M0 클러스터로 시작할 수 있습니다.
- GitHub 계정과 GitHub Copilot 사용 권한. Agent SDK가 Copilot 계정으로 인증합니다.
- Docker 호환 컨테이너 엔진. Aspire가 MarkItDown MCP Server 컨테이너를 실행합니다.

## 개발 환경

가장 간단한 방법은 VS Code에서 저장소를 열고 **Reopen in Container**를 선택하는 것입니다. Dev Container에는 .NET 10 SDK, Aspire CLI, Docker-in-Docker, Python, Node.js 및 필요한 VS Code 확장이 준비됩니다. 웹 UI 포트 `5087`도 자동 포워딩됩니다.

컨테이너를 사용하지 않는다면 로컬에 .NET 10 SDK, Aspire CLI, Docker 호환 엔진을 설치합니다. 원격 개발 환경에서는 VS Code에서 포트 `5087`을 포워딩해야 합니다. 샘플 메모리 데이터를 넣을 때는 Python 3.10 이상과 `scripts/memory/requirements.txt`의 의존성이 추가로 필요합니다.

## 선택 기능

- Copilot Chat에서 데이터를 조회하려면 MongoDB for VS Code Extension의 내장 MCP Server를 사용합니다. Extension은 Dev Container에 포함돼 있으며, 이 기능을 위해 Node.js를 별도로 설치할 필요는 없습니다.
- 데이터베이스를 조회 전용으로 제한하려면 Atlas에서 읽기 전용 DB 사용자를 별도로 만들 수 있습니다. 설정은 [설정 및 실행 가이드](SETUP.md#6-선택-copilot-chat에서-메모리-들여다보기)에 있습니다.
