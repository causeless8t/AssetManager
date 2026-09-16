# 기본 사용 예제

`ResourceManager`를 일반 객체로 생성하고 초기화, 패치, 프리팹 로드와 해제를 수행하는 예제입니다.

## 사용 방법

1. Package Manager에서 AssetManager의 **Samples** 탭을 엽니다.
2. **Basic Usage**를 프로젝트로 가져옵니다.
3. 빈 GameObject를 만들고 `AssetManagerExample`을 추가합니다.
4. Inspector에서 다음 값을 설정합니다.

| 항목 | 설명 |
| --- | --- |
| Remote URL | `filesinfo.dat`과 번들이 업로드된 CDN 디렉터리입니다. 패치를 사용하지 않으면 비워 둡니다. |
| Bundle Label | AssetBundle 빌드 창에서 대상 폴더에 지정한 레이블입니다. |
| Asset Path | 번들에 포함된 프리팹의 프로젝트 경로입니다. |
| Instance Parent | 생성된 프리팹의 부모 Transform입니다. 선택 사항입니다. |

예를 들어 Asset Path에는 다음과 같이 입력합니다.

```text
Assets/GameAssets/Prefabs/Character.prefab
```

초기 배포 번들은 실행 플랫폼에 맞게 빌드한 뒤 다음 경로에 배치해야 합니다.

```text
Assets/StreamingAssets/contents
```

이 폴더에는 AssetBundle 파일과 같은 빌드에서 생성된 `filesinfo.dat`이 함께 있어야 합니다.

## 수명 관리

예제의 `MonoBehaviour`가 `ResourceManager`의 수명을 소유합니다. 오브젝트가 파괴되면 진행 중인 패치를 취소하고 `UnloadAll(true)`로 로드된 번들을 해제합니다.

실제 프로젝트에서는 게임의 전역 수명 객체나 DI 컨테이너가 하나의 `ResourceManager` 인스턴스를 관리하는 방식을 권장합니다. 패키지 자체는 싱글턴을 강제하지 않습니다.
