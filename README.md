# AssetManager

AssetBundle 빌드, 원격 패치, CRC32 무결성 검증, 캐시 및 에셋 로딩을 제공하는 Unity Package Manager 패키지입니다.

## 요구 사항

- Unity 2022.3 이상
- 외부 패키지 의존성 없음

## 설치

Unity Package Manager에서 **Add package from git URL...**을 선택하고 다음 주소를 입력합니다.

```text
https://github.com/causeless8t/AssetManager.git
```

특정 버전을 사용하려면 태그를 지정합니다.

```text
https://github.com/causeless8t/AssetManager.git#2.2.0
```

## 주요 구성

- `ResourceManager`: 번들 및 에셋 로드, 캐시, 인스턴스 생성과 해제
- `AssetBundleUpdater`: 매니페스트 비교, 변경 파일 다운로드와 검증
- `AssetBundleBuilder`: 증분 AssetBundle 빌드와 매니페스트 생성
- `BuildAssetBundles`: 빌드 설정을 편집하고 실행하는 EditorWindow

`ResourceManager`는 일반 객체입니다. 애플리케이션에서는 하나의 인스턴스를 생성해 전역 수명으로 관리하는 방식을 권장합니다.

Android에서는 APK 또는 AAB 내부의 StreamingAssets 번들을
`UnityWebRequestAssetBundle`로 읽습니다. 패치로 내려받아
`Application.persistentDataPath`에 저장된 번들은 파일에서 직접 로드합니다.

빌드할 모든 대상 폴더는 한 번의 AssetBundle 빌드에 포함됩니다.
생성된 `filesinfo.dat`에는 번들별 의존성이 기록되며,
`ResourceManager`는 대상 번들보다 의존 번들을 먼저 로드합니다.
공유 의존 번들은 다른 번들에서 사용 중인 동안 해제되지 않습니다.

패치 다운로드는 `CancellationToken`으로 중단할 수 있습니다.

```csharp
using var cancellation = new CancellationTokenSource();

await resourceManager.CheckUpdateAsync(
    progress => Debug.Log($"패치 진행률: {progress:P0}"),
    cancellation.Token);
```

사용자가 취소하면 `OperationCanceledException`, 네트워크 요청이 실패하면
`IOException`, 파일 크기나 CRC가 일치하지 않으면 `InvalidDataException`이 발생합니다.
파일 교체가 시작된 이후에는 번들과 매니페스트의 일관성을 위해 교체 작업을 완료합니다.

## 빌드 창

Unity Editor에서 다음 메뉴를 선택합니다.

```text
Tools > Build Asset Bundle
```

빌드 대상 폴더와 레이블, 플랫폼별 앱 버전·리비전, 출력 경로를 설정할 수 있습니다.

## 저장소 구조

```text
AssetManager/
├── Runtime/
├── Editor/
├── CHANGELOG.md
├── LICENSE
├── README.md
└── package.json
```

## 라이선스

이 프로젝트는 [MIT 라이선스](LICENSE)를 따릅니다.
