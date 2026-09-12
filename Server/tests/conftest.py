import os


def pytest_configure() -> None:
    """Keep the default suite offline even when the developer shell uses OpenAI mode."""
    if os.getenv("RUN_DEEPSEEK_INTEGRATION") != "1":
        os.environ["AIFARM_PROVIDER"] = "mock"
        os.environ["AIFARM_IGNORE_SAVED_CONFIG"] = "1"
