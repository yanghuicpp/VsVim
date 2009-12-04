﻿using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VimCore;
using System.Windows.Input;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text;
using VimCoreTest.Utils;

namespace VimCoreTest
{
    [TestClass]
    public class MotionCaptureTest
    {
        static string[] s_lines = new string[]
            {
                "summary description for this line",
                "some other line",
                "running out of things to make up"
            };

        ITextBuffer _buffer = null;
        ITextSnapshot _snapshot = null;

        [TestInitialize]
        public void Init()
        {
            Initialize(s_lines);
        }

        public void Initialize(params string[] lines)
        {
            _buffer = Utils.EditorUtil.CreateBuffer(lines);
            _snapshot = _buffer.CurrentSnapshot;
        }

        internal MotionResult Process(int startPosition, int count, string input)
        {
            Assert.IsTrue(count > 0, "this will cause an almost infinite loop");
            var res = MotionCapture.ProcessInput(
                new SnapshotPoint(_snapshot, startPosition),
                InputUtil.CharToKeyInput(input[0]),
                count);
            foreach (var cur in input.Skip(1))
            {
                Assert.IsTrue(res.IsNeedMoreInput);
                var needMore = (MotionResult.NeedMoreInput)res;
                res = needMore.Item.Invoke(InputUtil.CharToKeyInput(cur));
            }

            return res;
       }


        [TestMethod]
        public void Word1()
        {
            Initialize("foo bar");
            var res = MotionCapture.ProcessInput(new SnapshotPoint(_snapshot, 0), InputUtil.KeyToKeyInput(Key.W), 1);
            Assert.IsTrue(res.IsComplete);
            var res2 = (MotionResult.Complete)res;
            var span = res2.Item.Item1;
            Assert.AreEqual(4, span.Length);
            Assert.AreEqual("foo ", span.GetText());
            Assert.AreEqual(MotionKind.Exclusive, res2.Item.Item2);
            Assert.AreEqual(OperationKind.CharacterWise, res2.Item.Item3);
        }


        [TestMethod]
        public void Word2()
        {
            Initialize("foo bar");
            var res = MotionCapture.ProcessInput(new SnapshotPoint(_snapshot, 1), InputUtil.KeyToKeyInput(Key.W), 1);
            Assert.IsTrue(res.IsComplete);
            var span = res.AsComplete().Item.Item1;
            Assert.AreEqual(3, span.Length);
            Assert.AreEqual("oo ", span.GetText());
        }

        [TestMethod, Description("Word motion with a count")]
        public void Word3()
        {
            Initialize("foo bar baz");
            var res = Process(0, 2, "w");
            Assert.IsTrue(res.IsComplete);
            Assert.AreEqual("foo bar ", res.AsComplete().Item.Item1.GetText());
        }

        [TestMethod, Description("Count across lines")]
        public void Word4()
        {
            Initialize("foo bar", "baz jaz");
            var res = Process(0, 3, "w");
            Assert.IsTrue(res.IsComplete);
            Assert.AreEqual("foo bar" + Environment.NewLine + "baz ", res.AsComplete().Item.Item1.GetText());
        }

        [TestMethod, Description("Count off the end of the buffer")]
        public void Word5()
        {
            Initialize("foo bar");
            var res = Process(0, 10, "w");
            Assert.IsTrue(res.IsComplete);
            Assert.AreEqual("foo bar", res.AsComplete().Item.Item1.GetText());
        }

        [TestMethod]
        public void BadInput()
        {
            Initialize("foo bar");
            var res = MotionCapture.ProcessInput(new SnapshotPoint(_snapshot, 0), InputUtil.KeyToKeyInput(Key.Z), 0);
            Assert.IsTrue(res.IsInvalidMotion);
            res = res.AsInvalidMotion().Item2.Invoke(InputUtil.KeyToKeyInput(Key.Escape));
            Assert.IsTrue(res.IsCancel);
        }


        [TestMethod, Description("Keep gettnig input until it's escaped")]
        public void BadInput2()
        {
            Initialize("foo bar");
            var res = MotionCapture.ProcessInput(new SnapshotPoint(_snapshot, 0), InputUtil.KeyToKeyInput(Key.Z), 0);
            Assert.IsTrue(res.IsInvalidMotion);
            res = res.AsInvalidMotion().Item2.Invoke(InputUtil.KeyToKeyInput(Key.A));
            Assert.IsTrue(res.IsInvalidMotion);
            res = res.AsInvalidMotion().Item2.Invoke(InputUtil.KeyToKeyInput(Key.Escape));
            Assert.IsTrue(res.IsCancel);
        }

        [TestMethod]
        public void EndOfLine1()
        {
            Initialize("foo bar", "baz");
            var ki = InputUtil.CharToKeyInput('$');
            var res = MotionCapture.ProcessInput(new SnapshotPoint(_snapshot, 0), ki, 0);
            Assert.IsTrue(res.IsComplete);
            var span = res.AsComplete().Item.Item1;
            Assert.AreEqual("foo bar", span.GetText());
            var tuple = res.AsComplete().Item;
            Assert.AreEqual(MotionKind.Inclusive, tuple.Item2);
            Assert.AreEqual(OperationKind.CharacterWise, tuple.Item3);
        }

        [TestMethod]
        public void EndOfLine2()
        {
            Initialize("foo bar", "baz");
            var ki = InputUtil.CharToKeyInput('$');
            var res = MotionCapture.ProcessInput(new SnapshotPoint(_snapshot, 1), ki, 0);
            Assert.IsTrue(res.IsComplete);
            var span = res.AsComplete().Item.Item1;
            Assert.AreEqual("oo bar", span.GetText());
        }

        [TestMethod]
        public void EndOfLineCount1()
        {
            Initialize("foo", "bar", "baz");
            var ki = InputUtil.CharToKeyInput('$');
            var res = MotionCapture.ProcessInput(new SnapshotPoint(_snapshot, 0), ki, 2);
            Assert.IsTrue(res.IsComplete);
            var tuple = res.AsComplete().Item;
            Assert.AreEqual("foo" + Environment.NewLine + "bar", tuple.Item1.GetText());
            Assert.AreEqual(MotionKind.Inclusive, tuple.Item2);
            Assert.AreEqual(OperationKind.CharacterWise, tuple.Item3);
        }

        [TestMethod]
        public void EndOfLineCount2()
        {
            Initialize("foo", "bar", "baz", "jar");
            var ki = InputUtil.CharToKeyInput('$');
            var res = MotionCapture.ProcessInput(new SnapshotPoint(_snapshot, 0), ki, 3);
            Assert.IsTrue(res.IsComplete);
            var tuple = res.AsComplete().Item;
            Assert.AreEqual("foo" + Environment.NewLine + "bar" + Environment.NewLine +"baz", tuple.Item1.GetText());
            Assert.AreEqual(MotionKind.Inclusive, tuple.Item2);
            Assert.AreEqual(OperationKind.CharacterWise, tuple.Item3);
        }

        [TestMethod,Description("Make sure counts past the end of the buffer don't crash")]
        public void EndOfLineCount3()
        {
            Initialize("foo");
            var ki = InputUtil.CharToKeyInput('$');
            var res = MotionCapture.ProcessInput(new SnapshotPoint(_snapshot, 0), ki, 300);
            Assert.IsTrue(res.IsComplete);
            var tuple = res.AsComplete().Item;
            Assert.AreEqual("foo", tuple.Item1.GetText());
        }

        [TestMethod]
        public void StartOfLine1()
        {
            Initialize("foo");
            var ki = InputUtil.CharToKeyInput('^');
            var res = MotionCapture.ProcessInput(_buffer.CurrentSnapshot.GetLineFromLineNumber(0).End, ki, 1);
            Assert.IsTrue(res.IsComplete);
            var tuple = res.AsComplete().Item;
            Assert.AreEqual("foo", tuple.Item1.GetText());
            Assert.AreEqual(MotionKind.Exclusive, tuple.Item2);
            Assert.AreEqual(OperationKind.CharacterWise, tuple.Item3);
        }

        [TestMethod, Description("Make sure it goes to the first non-whitespace character")]
        public void StartOfLine2()
        {
            Initialize("  foo");
            var ki = InputUtil.CharToKeyInput('^');
            var res = MotionCapture.ProcessInput(_buffer.CurrentSnapshot.GetLineFromLineNumber(0).End, ki, 1);
            Assert.IsTrue(res.IsComplete);
            var tuple = res.AsComplete().Item;
            Assert.AreEqual("foo", tuple.Item1.GetText());
            Assert.AreEqual(MotionKind.Exclusive, tuple.Item2);
            Assert.AreEqual(OperationKind.CharacterWise, tuple.Item3);
        }

        [TestMethod]
        public void Count1()
        {
            Initialize("foo bar baz");
            var res  = Process(0, 1, "2w");
            Assert.IsTrue(res.IsComplete);
            var span = res.AsComplete().Item.Item1;
            Assert.AreEqual("foo bar ", span.GetText());
        }

        [TestMethod, Description("Count of 1")]
        public void Count2()
        {
            Initialize("foo bar baz");
            var res = Process(0, 1, "1w");
            Assert.IsTrue(res.IsComplete);
            var span = res.AsComplete().Item.Item1;
            Assert.AreEqual("foo ", span.GetText());
        }

        [TestMethod]
        public void AllWord1()
        {
            Initialize("foo bar");
            var res = Process(0, 1, "aw");
            Assert.IsTrue(res.IsComplete);
            Assert.AreEqual("foo ", res.AsComplete().Item.Item1.GetText());
        }

        [TestMethod]
        public void AllWord2()
        {
            Initialize("foo bar");
            var res = Process(1, 1, "aw");
            Assert.IsTrue(res.IsComplete);
            Assert.AreEqual("foo ", res.AsComplete().Item.Item1.GetText());
        }

        [TestMethod]
        public void AllWord3()
        {
            Initialize("foo bar baz");
            var res = Process(1, 1, "2aw");
            Assert.IsTrue(res.IsComplete);
            Assert.AreEqual("foo bar ", res.AsComplete().Item.Item1.GetText());
        }

        [TestMethod]
        public void CharLeft1()
        {
            Initialize("foo bar");
            var res = Process(2, 1, "2h");
            Assert.IsTrue(res.IsComplete);
            Assert.AreEqual("fo", res.AsComplete().Item.Item1.GetText());
        }

        [TestMethod, Description("Make sure that counts are multiplied")]
        public void CharLeft2()
        {
            Initialize("food bar");
            var res = Process(4, 2, "2h");
            Assert.AreEqual("food", res.AsComplete().Item.Item1.GetText());
        }

        [TestMethod]
        public void CharRight1()
        {
            Initialize("foo");
            var res = Process(0, 1, "2l");
            Assert.AreEqual("fo", res.AsComplete().Item.Item1.GetText());
            Assert.AreEqual(OperationKind.CharacterWise, res.AsComplete().Item.Item3);
        }

        [TestMethod]
        public void LineUp1()
        {
            Initialize("foo", "bar");
            var res = Process(_snapshot.GetLineFromLineNumber(1).Start.Position, 1, "k");
            Assert.AreEqual(OperationKind.LineWise, res.AsComplete().Item.Item3);
            Assert.AreEqual("foo" + Environment.NewLine + "bar", res.AsComplete().Item.Item1.GetText());
        }
    }   
}