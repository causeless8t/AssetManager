# CLAUDE.md

이 문서는 AI 코딩 도구가 AssetManager 저장소를 수정할 때 따라야 할 기준을 설명합니다.

## 프로젝트 개요

AssetManager는 Unity 프로젝트에서 다음 기능을 제공하는 Unity Package Manager 패키지입니다.

- 폴더 단위 AssetBundle 빌드
- 번들 매니페스트 생성과 원격 패치
- 파일 크기와 CRC32 기반 무결성 검증
- 번들 의존성 로드와 공유 수명 관리
- 번들 및 에셋 캐시
- Android와 WebGL StreamingAssets 지원

지원하는 최소 Unity 버전은 Unity 2022.3입니다.

## 저장소 구조

```text
AssetManager/
├── Runtime/       # 런타임 로드, 패치, 캐시, 매니페스트
├── Editor/        # AssetBundle 빌드 설정과 빌드 도구
├── Samples~/      # Package Manager에서 가져오는 사용 예제
├── CHANGELOG.md
├── LICENSE
├── README.md
└── package.json
```

저장소 자체가 UPM 패키지입니다. `Assets/Deploy` 같은 하위 배포 패키지 구조를 다시 만들지 않습니다.

## 핵심 클래스

### `ResourceManager`

- 사용자에게 공개되는 런타임 진입점입니다.
- 일반 C# 객체이며 싱글턴을 상속하거나 자체 정적 인스턴스를 보유하지 않습니다.
- 생성과 전역 수명 관리는 패키지 사용자가 결정합니다.
- 번들, 에셋, 진행 중인 비동기 작업을 캐시합니다.
- 대상 번들보다 의존 번들을 먼저 로드합니다.
- 공유 의존 번들은 다른 캐시 번들이 참조하는 동안 해제하지 않습니다.

### `AssetBundleUpdater`

- `ResourceManager` 내부에서만 사용하는 구현 클래스입니다.
- 초기 매니페스트 로드, 원격 매니페스트 비교, 다운로드와 검증을 담당합니다.
- 다운로드 파일은 `.download`에 먼저 저장하고 전체 검증이 끝난 뒤 교체합니다.
- 번들 교체가 시작되면 이전 매니페스트와 새 번들이 섞이지 않도록 매니페스트 교체까지 완료합니다.

### `AssetBundleBuilder`

- Unity Editor 전용 빌드 파이프라인입니다.
- 모든 `AssetBundleBuild` 정의를 한 번의 `BuildPipeline.BuildAssetBundles` 호출에 전달합니다.
- 이 원칙을 깨면 Unity가 번들 간 의존성을 올바르게 계산할 수 없습니다.
- Unity의 직접 의존성을 `filesinfo.dat`에 기록합니다.

### `ContentsInfoList`

- 로컬과 원격에서 공유하는 JSON 매니페스트 모델입니다.
- 번들 경로, 레이블, CRC32, 파일 크기와 직접 의존성을 기록합니다.
- 기존 매니페스트에 `Dependencies`가 없을 수 있으므로 역직렬화 결과가 `null`인 경우를 허용해야 합니다.

## 반드시 유지할 설계 원칙

### 외부 의존성

- UniTask, Addressables, UnityCore 또는 프로젝트 전용 패키지를 추가하지 않습니다.
- 비동기 API는 `System.Threading.Tasks.Task`와 `CancellationToken`을 사용합니다.
- Runtime asmdef의 외부 참조는 비어 있어야 합니다.

### 객체 수명

- `ResourceManager`를 패키지 내부 싱글턴으로 변경하지 않습니다.
- `MonoBehaviour`, 서비스 로케이터 또는 DI 컨테이너와의 결합을 Runtime에 추가하지 않습니다.
- 애플리케이션 전역 인스턴스가 필요하면 패키지 사용자가 관리합니다.

### StreamingAssets

- Android의 StreamingAssets 경로는 `jar:file://...!/assets` 형식의 URL입니다.
- Android와 WebGL의 패키지 내부 번들은 `UnityWebRequestAssetBundle`로 읽습니다.
- `Application.persistentDataPath`에 다운로드된 번들은 `AssetBundle.LoadFromFileAsync`로 읽습니다.
- Android StreamingAssets URL을 `File` API나 `AssetBundle.LoadFromFileAsync`에 전달하지 않습니다.

### 패치 안정성

- 다운로드한 데이터는 크기와 CRC32를 모두 검증해야 합니다.
- 검증이 끝나기 전에 기존 번들과 매니페스트를 교체하지 않습니다.
- 한 파일이 실패하면 같은 배치의 나머지 요청도 취소합니다.
- 취소 또는 실패 시 `.download` 임시 파일을 제거합니다.
- 사용자 취소는 `OperationCanceledException`으로 전달합니다.
- 네트워크 및 파일 요청 실패는 `IOException`, 무결성 실패는 `InvalidDataException`으로 구분합니다.
- 최종 파일 교체가 시작된 이후에는 패치 일관성을 위해 매니페스트 교체까지 완료합니다.

### 번들 의존성

- 매니페스트에는 Unity가 반환한 직접 의존성만 저장합니다.
- 런타임에서는 의존성을 재귀적으로 먼저 로드합니다.
- 진행 중인 동일 번들 로드 작업은 공유해야 합니다.
- 순환 의존성은 `InvalidDataException`으로 차단합니다.
- 명시적으로 로드했거나 다른 번들이 참조하는 번들을 먼저 해제하지 않습니다.

### 진행률

- 패치 진행률은 완료된 파일 개수가 아니라 전체 수신 바이트를 기준으로 계산합니다.
- 병렬 요청의 진행률을 합산할 때 값이 감소하거나 `1.0`을 초과하지 않도록 합니다.
- 다운로드할 파일이 없어도 성공적으로 갱신되면 `1.0`을 전달합니다.

## 코딩 규칙

- 공개 API의 인자 검증과 예외 메시지는 구체적으로 작성합니다.
- Runtime 코드에서 `Debug.Log`를 직접 호출하지 않습니다.
- Editor 도구와 Sample에서는 사용자 피드백을 위해 Unity 로그를 사용할 수 있습니다.
- 패키지 내부 구현은 필요하지 않은 경우 `internal`로 유지합니다.
- 파일과 URL 경로를 혼용하지 않습니다.
- 사용자 입력으로 받은 상대 경로에서 `..` 경로 이동을 허용하지 않습니다.
- 새 Unity 파일이나 폴더를 추가하면 대응하는 `.meta` 파일도 커밋합니다.
- 문서와 사용자 대상 설명은 한글로 작성합니다.

## 공개 API 변경

공개 API를 변경할 때는 다음 항목을 함께 확인합니다.

1. 기존 호출 코드가 계속 컴파일되는지 확인합니다.
2. `README.md`의 예제를 수정합니다.
3. `CHANGELOG.md`에 변경 이유와 호환성 영향을 기록합니다.
4. 변경 성격에 맞게 `package.json` 버전을 갱신합니다.
5. `Samples~/BasicUsage`가 새 API와 일치하는지 확인합니다.

## 검증 절차

현재 저장소에는 자동화된 Unity Test Framework 프로젝트가 포함되어 있지 않습니다. 변경 후 최소한 다음 항목을 확인합니다.

1. `package.json`과 모든 asmdef가 올바른 JSON인지 검사합니다.
2. 공백 오류와 누락된 `.meta` 파일을 확인합니다.
3. Unity 2022.3 프로젝트에서 Git URL로 패키지를 설치합니다.
4. Runtime과 Editor asmdef가 오류 없이 컴파일되는지 확인합니다.
5. Android 및 iOS 번들을 빌드하고 `filesinfo.dat`의 경로, 크기, CRC와 의존성을 확인합니다.
6. Android에서 StreamingAssets 초기 번들을 로드합니다.
7. 원격 패치 성공, 사용자 취소, CRC 불일치와 네트워크 실패를 각각 확인합니다.
8. 공유 의존성을 사용하는 두 번들을 로드한 뒤 하나씩 해제해 수명을 확인합니다.
9. Package Manager에서 Basic Usage 샘플을 가져와 컴파일되는지 확인합니다.

Unity Editor를 실행할 수 없는 환경에서는 실행 검증을 했다고 주장하지 말고, 수행하지 못한 항목을 결과에 명시합니다.

## 작업 범위 주의 사항

- 요청받지 않은 프로젝트 전용 UI, 로그인, 분석 SDK 또는 서버 규칙을 패키지에 포함하지 않습니다.
- 실행 가능한 CDN과 테스트 번들이 준비되지 않은 상태에서 형식적인 샘플 씬을 추가하지 않습니다.
- 기능 수보다 작은 공개 API, 명확한 책임 분리와 실패 시 데이터 보존을 우선합니다.
