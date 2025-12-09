"""데모 실행 스크립트 - 대화형 게임 용어 인식 시스템"""
import sys
from pathlib import Path

# src 디렉토리를 경로에 추가
sys.path.insert(0, str(Path(__file__).parent / "src"))

from inference import GameTermRecognizer


def interactive_demo():
    """대화형 데모"""
    project_root = Path(__file__).parent
    
    model_path = project_root / "models" / "final_model"
    term_dict_path = project_root / "data" / "term_index.pkl"
    
    # 모델 존재 확인
    if not model_path.exists():
        print("❌ 학습된 모델을 찾을 수 없습니다.")
        print("\n다음 단계를 순서대로 실행해주세요:")
        print("  1. 원본 데이터를 data/raw/ 에 배치")
        print("  2. python src/preprocess.py")
        print("  3. python src/train.py")
        return
    
    print("=" * 80)
    print("🎮 게임 용어 인식 및 정의 설명 시스템")
    print("=" * 80)
    print("\n문장을 입력하면 게임 용어를 자동으로 인식하고 정의를 제공합니다.")
    print("종료하려면 'quit' 또는 'exit'를 입력하세요.\n")
    
    # 인식기 초기화
    recognizer = GameTermRecognizer(
        model_path=str(model_path),
        term_dict_path=str(term_dict_path) if term_dict_path.exists() else None
    )
    
    print("\n✅ 준비 완료! 문장을 입력해주세요.\n")
    
    while True:
        try:
            # 사용자 입력
            sentence = input("문장 입력: ").strip()
            
            # 종료 명령
            if sentence.lower() in ['quit', 'exit', '종료']:
                print("\n👋 프로그램을 종료합니다.")
                break
            
            # 빈 입력 무시
            if not sentence:
                continue
            
            # 용어 인식 및 설명
            results = recognizer.recognize_and_explain(sentence)
            
            # 결과 출력
            print("\n" + recognizer.format_output(sentence, results))
            print()
            
        except KeyboardInterrupt:
            print("\n\n👋 프로그램을 종료합니다.")
            break
        except Exception as e:
            print(f"\n⚠️  오류 발생: {e}\n")


def batch_demo():
    """배치 데모 - 미리 정의된 문장들 처리"""
    project_root = Path(__file__).parent
    
    model_path = project_root / "models" / "final_model"
    term_dict_path = project_root / "data" / "term_index.pkl"
    
    if not model_path.exists():
        print("❌ 학습된 모델을 찾을 수 없습니다.")
        return
    
    print("=" * 80)
    print("🎮 게임 용어 인식 시스템 - 배치 모드")
    print("=" * 80)
    
    # 인식기 초기화
    recognizer = GameTermRecognizer(
        model_path=str(model_path),
        term_dict_path=str(term_dict_path) if term_dict_path.exists() else None
    )
    
    # 테스트 문장들
    test_sentences = [
        "퀘스트 사냥꾼은 대체로 컨트롤 성향 덱에 더 강한 모습을 보인다.",
        "나이트 페이 성약의 단 이동 연결망 1단계를 강화하면 그루터로 이동할 수 있다.",
        "딜칭호는 아마 설 즈음에 풀릴거 같구요",
        "토마가 보호막 + 불원소 부여 역할로 쓸만하지 않을까 싶습니다.",
        "라이아 보스는 강력한 공격 패턴을 가지고 있다.",
    ]
    
    print("\n🧪 테스트 문장 처리 중...\n")
    
    for i, sentence in enumerate(test_sentences, 1):
        print(f"\n{'='*80}")
        print(f"테스트 {i}/{len(test_sentences)}")
        print(f"{'='*80}")
        
        results = recognizer.recognize_and_explain(sentence)
        print(recognizer.format_output(sentence, results))


def main():
    """메인 함수"""
    import argparse
    
    parser = argparse.ArgumentParser(description="게임 용어 인식 데모")
    parser.add_argument(
        '--mode',
        choices=['interactive', 'batch'],
        default='interactive',
        help='실행 모드 선택 (기본: interactive)'
    )
    
    args = parser.parse_args()
    
    if args.mode == 'interactive':
        interactive_demo()
    else:
        batch_demo()


if __name__ == "__main__":
    main()
