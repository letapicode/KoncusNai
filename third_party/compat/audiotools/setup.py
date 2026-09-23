from setuptools import find_packages
from setuptools import setup

with open("README.md") as f:
    long_description = f.read()

setup(
    name="descript-audiotools",
    version="0.7.4+koncus1",
    classifiers=[
        "Intended Audience :: Developers",
        "Intended Audience :: Education",
        "Intended Audience :: Science/Research",
        "Natural Language :: English",
        "Programming Language :: Python :: 3.7",
        "Topic :: Artistic Software",
        "Topic :: Multimedia",
        "Topic :: Multimedia :: Sound/Audio",
        "Topic :: Multimedia :: Sound/Audio :: Editors",
        "Topic :: Software Development :: Libraries",
    ],
    description="Utilities for handling audio.",
    long_description=long_description,
    long_description_content_type="text/markdown",
    author="Prem Seetharaman, Lucas Gestin",
    author_email="prem@descript.com",
    license="MIT",
    packages=find_packages(),
    package_data={
        "": [
            "core/templates/headers.html",
            "core/templates/widget.html",
            "core/templates/pandoc.css",
        ]
    },
    install_requires=[
        "argbind",
        "numpy",
        "soundfile",
        "pyloudnorm",
        "importlib-resources",
        "scipy",
        "torch",
        "julius",
        "ffmpy",
        "ipython",
        "rich",
        "matplotlib",
        "librosa",
        "pystoi",
        "flatten-dict",
        "markdown2",
        "randomname",
        # Koncus Nai compatibility patch: AudioTools does not import protobuf on
        # its inference path, and 7.36.2 is covered by the isolated runtime test.
        "protobuf == 7.36.2",
        "tensorboard",
        "tqdm",
    ],
    extras_require={
        "tests": [
            "pytest",
            "pytest-cov",
            "line_profiler",
            "pesq",
            "gradio==3.32.0",
            "transformers>=4.23.1",
        ],
        "docs": [
            "sphinx",
            "sphinx-rtd-theme",
            "myst-parser",
            "myst-nb",
            "sphinx-multiversion",
        ],
        "whisper": [
            "transformers>=4.23.1",
        ],
    },
)
