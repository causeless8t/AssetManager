# 변경 이력

이 프로젝트의 주요 변경 사항을 기록합니다.

문서 형식은 [변경 이력 유지하기](https://keepachangelog.com/ko/1.0.0/)를 따르며,
버전은 [유의적 버전](https://semver.org/lang/ko/) 규칙을 따릅니다.

## [2.0.1] - 2026-09-16

### 수정

- Android StreamingAssets의 AssetBundle을 파일 API가 아닌
  `UnityWebRequestAssetBundle`로 읽도록 수정했습니다.

## [2.0.0] - 2026-09-16

### 변경

- 저장소를 루트 Unity Package Manager 패키지 구조로 변경했습니다.
- 패키지 범위를 자체 AssetBundle 빌드·패치·로드 기능으로 명확히 했습니다.
- `ResourceManager`를 일반 객체로 변경하고 UnityCore 싱글턴 의존성을 제거했습니다.
- UniTask를 표준 `Task` 기반 API로 교체했습니다.
- 원격 패치 처리를 내부 `AssetBundleUpdater`로 분리했습니다.
- EditorWindow에서 빌드 설정과 빌드 파이프라인을 분리했습니다.
- 파일 CRC뿐 아니라 Unity 에셋 의존성 해시를 이용해 증분 빌드를 판단하도록 변경했습니다.
- 동일 번들과 에셋의 진행 중인 로드 작업을 공유하도록 변경했습니다.
- 개별 번들 해제 API를 추가했습니다.

### 수정

- AssetBundle 바이너리를 JSON 매니페스트로 파싱하던 문제를 수정했습니다.
- 일부 다운로드가 실패해도 로컬 매니페스트가 교체될 수 있던 문제를 수정했습니다.
- Android StreamingAssets 매니페스트를 파일 API로 읽던 문제를 수정했습니다.
- 삭제된 에셋이 증분 빌드 스냅샷에 계속 남던 문제를 수정했습니다.
- 에디터에서 부분 문자열로 잘못된 에셋을 선택할 수 있던 문제를 수정했습니다.
- `ContentsInfo.CompareTo()`가 정렬이 아닌 동일성 검사로 사용되던 문제를 수정했습니다.

### 제거

- Addressables 지원과 프로젝트 전용 UI·분석 의존성을 제거했습니다.
- UnityCore 의존성을 제거했습니다.
- UniTask 의존성을 제거했습니다.
- 저장소에 포함된 테스트용 Unity 프로젝트 구조를 제거했습니다.

## [1.0.0] - 2024-12-27

### 추가

- 최초 `ResourceManager`와 AssetBundle 빌드 도구를 추가했습니다.
