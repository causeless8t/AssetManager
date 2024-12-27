# AssetManager
Addressable, AssetBundle 을 관리하는 편의기능을 담은 패키지
- Package 경로 : https://github.com/causeless8t/AssetManager.git?path=Assets/Deploy

- 주의 사항
1. (AssetBundle) 번들을 빌드하기 위한 윈도우에서 빌드할 폴더 목록, 플랫폼 별 버전, CDN 버전, 빌드될 경로를 저장해두고 빌드를 진행한다.
2. Addressable 의 경우에는 USE_ADDRESSABLE 디파인을 추가해야 한다.
3. (AssetBundle) 빌드된 파일명은 편의에 따라 {번들폴더 루트}~{하위경로}.unity3d 가 된다.
4. (AssetBundle) 빌드된 파일과 함께 생성된 fileinfo.dat 파일을 함께 원격지에 업로드한다.
5. (AssetBundle) CDN버전을 0으로 기록하면 통빌드이므로 추후 빌드 시 StreamingAssets 폴더에 옮기는 작업이 필요하다.
6. (AssetBundle) 함께 기록하는 Label은 Label을 통해 번들을 로딩을 하고자 할때 사용되며, 기록하지 않을 시 파일명을 정확히 입력해야 로딩할 수 있다.