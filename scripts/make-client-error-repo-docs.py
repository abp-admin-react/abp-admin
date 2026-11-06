from pathlib import Path
import re

root = Path(__file__).resolve().parents[1] / "docs"
files = [
    "client-error-handling-development-plan.md",
    "client-error-handling-development-plan-review-disposition.md",
    "client-error-handling-development-plan-review-disposition-r2.md",
    "client-error-handling-development-plan-review-hy4.md",
    "client-error-handling-development-plan-review-hy4-r2.md",
    "client-error-handling-development-plan-review-grok.md",
    "client-error-handling-development-plan-review-qwen.md",
    "client-error-handling-development-plan-review-2-qwen.md",
    "client-error-handling-development-plan-review-qrok.md",
    "client-error-handling-development-plan-review-wide-qwen.md",
]


def transform(text: str) -> str:
    text = text.replace("E:/code/abp_admin/docs/", "./")
    text = text.replace("E:/code/abp_admin/", "../")
    text = text.replace("E:\\code\\abp_admin\\docs\\", "./")
    text = text.replace("E:\\code\\abp_admin\\", "../")
    text = re.sub(
        r"\[([^\]]+)\]\(E:/code/exchange/exchange/([^)]+)\)",
        r"\1（参考仓库 exchange/exchange `\2`）",
        text,
    )
    text = re.sub(
        r"E:/code/exchange/exchange/([^\s)]+)",
        r"exchange/exchange/\1",
        text,
    )
    return text


def main() -> None:
    for name in files:
        src = root / name
        dest = root / f"{src.stem}.repo.md"
        dest.write_text(transform(src.read_text(encoding="utf-8")), encoding="utf-8", newline="\n")
        print(dest.name)


if __name__ == "__main__":
    main()
