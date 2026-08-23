#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""NUnit3 XML → JUnit 扁平 XML 转换器(Phase 6.5 CI)

Unity -runTests 产出的 NUnit3 XML 是嵌套 test-suite 结构,
Jenkins JUnit 插件解析器只能处理单层嵌套(JENKINS-6545),
会报 "None of the test reports contained any result"。

本脚本把所有 <test-case> 拍平到一层 <testsuite>,输出标准 JUnit 格式:
- 用法: python tools/nunit3_to_junit.py <in.xml> <out.xml>
- 仅用标准库(xml.etree),零第三方依赖 —— 与 asset_check.py 同约束
"""

import sys
import xml.etree.ElementTree as ET


def convert(src: str, dst: str) -> int:
    tree = ET.parse(src)
    root = tree.getroot()

    # 收集所有 test-case,记下每个的 classname(取全名去掉最后的 .方法名)
    cases = []
    for tc in root.iter("test-case"):
        name = tc.get("name", "unnamed")
        full = tc.get("fullname", name)
        # fullname 形如 Box.Gameplay.Tests.L10nTests.Format_Replaces_Placeholders,
        # classname 取去掉最后一个段的部分
        cls = full.rsplit(".", 1)[0] if "." in full else ""
        result = tc.get("result", "Failed")  # Passed / Failed / Skipped 等
        time = tc.get("duration") or tc.get("time") or "0"
        failure = None
        for f in tc.iter("failure"):
            # 取失败信息(截断到 2000 字符,避免 Jenkins 解析大文本卡顿)
            failure = (f.findtext("message") or "").strip()[:2000]
            break
        cases.append((name, cls, result, time, failure))

    passed = sum(1 for c in cases if c[2] == "Passed")
    failed = sum(1 for c in cases if c[2] != "Passed")
    total = len(cases)

    # 构建扁平 JUnit XML(单层 testsuite,testcase 直接挂其上)
    suites = ET.Element("testsuites", {"tests": str(total),
                                       "failures": str(failed),
                                       "errors": "0"})
    suite = ET.SubElement(suites, "testsuite",
                          {"name": "Unity EditMode", "tests": str(total),
                           "failures": str(failed), "errors": "0",
                           "time": root.get("duration", "0")})
    for name, cls, result, time, failure in cases:
        attrs = {"classname": cls, "name": name, "time": time}
        if result == "Skipped":
            attrs["skipped"] = "1"
        tc = ET.SubElement(suite, "testcase", attrs)
        if failure is not None:
            ET.SubElement(tc, "failure", {"message": failure[:2000]}).text = failure

    # 缩进美化 + 声明,保证 Jenkins 解析稳定
    ET.indent(suites, space="  ")
    data = '<?xml version="1.0" encoding="utf-8"?>\n' + ET.tostring(
        suites, encoding="unicode", short_empty_elements=True) + "\n"
    with open(dst, "w", encoding="utf-8") as f:
        f.write(data)
    print(f"converted: {total} cases (passed={passed}, failed={failed}) -> {dst}")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    sys.exit(convert(sys.argv[1], sys.argv[2]))
