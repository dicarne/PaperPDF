# PaperPDF

基于MuPDF的快速PDF阅读器，支持手写笔记。

## 从源代码编译 · From Source

首先安装最新版的Visual Studio 2019与WPF套件。

然后安装`NuGet`依赖包。

然后点击生成。

手动复制PaperPDF目录下的libmupdf.dll到生成的应用程序`.exe`同级目录。

## 环境 · Environment
.Net Core 3.1

## 用户配置 · User Config
初次启动程序后，会在 `我的文档/PaperPDF` 下生成`paper_pdf_config.yaml`配置文件，修改此文件即可修改笔的排序、颜色、方案切换等。
当添加一个自定义方案后，默认方案将被隐藏。

Schema定义不同的方案，比如不同的配色方案，你可以在软件内切换不同的方案。

笔的种类只能选`Pen`或`HighLight`，分别对应普通笔和荧光笔。

## TODO
* 自由的书签
* 可定制的笔的粗细
