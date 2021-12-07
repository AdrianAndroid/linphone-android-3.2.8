/*
 * [The "BSD licence"]
 * Copyright (c) 2005-2008 Terence Parr
 * All rights reserved.
 *
 * Conversion to C#:
 * Copyright (c) 2008-2009 Sam Harwell, Pixel Mine, Inc.
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 * 1. Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 * 3. The name of the author may not be used to endorse or promote products
 *    derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
 * IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
 * IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
 * INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 * DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 * THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
 * THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

namespace Antlr.Runtime {
    using ConditionalAttribute = System.Diagnostics.ConditionalAttribute;
    using Console = System.Console;
    using IDebugEventListener = Antlr.Runtime.Debug.IDebugEventListener;

    public delegate int SpecialStateTransitionHandler(DFA dfa, int s, IIntStream input);

    /** <summary>A DFA implemented as a set of transition tables.</summary>
     *
     *  <remarks>
     *  Any state that has a semantic predicate edge is special; those states
     *  are generated with if-then-else structures in a specialStateTransition()
     *  which is generated by cyclicDFA template.
     *
     *  There are at most 32767 states (16-bit signed short).
     *  Could get away with byte sometimes but would have to generate different
     *  types and the simulation code too.  For a point of reference, the Java
     *  lexer's Tokens rule DFA has 326 states roughly.
     *  </remarks>
     */
    public class DFA {
        public DFA()
            : this(new SpecialStateTransitionHandler(SpecialStateTransitionDefault)) {
        }
        public DFA(SpecialStateTransitionHandler specialStateTransition) {
            this.SpecialStateTransition = specialStateTransition ?? new SpecialStateTransitionHandler(SpecialStateTransitionDefault);
        }

        protected short[] eot;
        protected short[] eof;
        protected char[] min;
        protected char[] max;
        protected short[] accept;
        protected short[] special;
        protected short[][] transition;

        protected int decisionNumber;

        /** <summary>Which recognizer encloses this DFA?  Needed to check backtracking</summary> */
        protected BaseRecognizer recognizer;

        public readonly bool debug = false;

        /** <summary>
         *  From the input stream, predict what alternative will succeed
         *  using this DFA (representing the covering regular approximation
         *  to the underlying CFL).  Return an alternative number 1..n.  Throw
         *  an exception upon error.
         *  </summary>
         */
        public virtual int Predict(IIntStream input) {
            if (debug) {
                Console.Error.WriteLine("Enter DFA.predict for decision " + decisionNumber);
            }
            int mark = input.Mark(); // remember where decision started in input
            int s = 0; // we always start at s0
            try {
                for (; ; ) {
                    if (debug)
                        Console.Error.WriteLine("DFA " + decisionNumber + " state " + s + " LA(1)=" + (char)input.LA(1) + "(" + input.LA(1) +
                                           "), index=" + input.Index);
                    int specialState = special[s];
                    if (specialState >= 0) {
                        if (debug) {
                            Console.Error.WriteLine("DFA " + decisionNumber +
                                " state " + s + " is special state " + specialState);
                        }
                        s = SpecialStateTransition(this, specialState, input);
                        if (debug) {
                            Console.Error.WriteLine("DFA " + decisionNumber +
                                " returns from special state " + specialState + " to " + s);
                        }
                        if (s == -1) {
                            NoViableAlt(s, input);
                            return 0;
                        }
                        input.Consume();
                        continue;
                    }
                    if (accept[s] >= 1) {
                        if (debug)
                            Console.Error.WriteLine("accept; predict " + accept[s] + " from state " + s);
                        return accept[s];
                    }
                    // look for a normal char transition
                    char c = (char)input.LA(1); // -1 == \uFFFF, all tokens fit in 65000 space
                    if (c >= min[s] && c <= max[s]) {
                        int snext = transition[s][c - min[s]]; // move to next state
                        if (snext < 0) {
                            // was in range but not a normal transition
                            // must check EOT, which is like the else clause.
                            // eot[s]>=0 indicates that an EOT edge goes to another
                            // state.
                            if (eot[s] >= 0) {  // EOT Transition to accept state?
                                if (debug)
                                    Console.Error.WriteLine("EOT transition");
                                s = eot[s];
                                input.Consume();
                                // TODO: I had this as return accept[eot[s]]
                                // which assumed here that the EOT edge always
                                // went to an accept...faster to do this, but
                                // what about predicated edges coming from EOT
                                // target?
                                continue;
                            }
                            NoViableAlt(s, input);
                            return 0;
                        }
                        s = snext;
                        input.Consume();
                        continue;
                    }
                    if (eot[s] >= 0) {  // EOT Transition?
                        if (debug)
                            Console.Error.WriteLine("EOT transition");
                        s = eot[s];
                        input.Consume();
                        continue;
                    }
                    if (c == unchecked((char)TokenTypes.EndOfFile) && eof[s] >= 0) {  // EOF Transition to accept state?
                        if (debug)
                            Console.Error.WriteLine("accept via EOF; predict " + accept[eof[s]] + " from " + eof[s]);
                        return accept[eof[s]];
                    }
                    // not in range and not EOF/EOT, must be invalid symbol
                    if (debug) {
                        Console.Error.WriteLine("min[" + s + "]=" + min[s]);
                        Console.Error.WriteLine("max[" + s + "]=" + max[s]);
                        Console.Error.WriteLine("eot[" + s + "]=" + eot[s]);
                        Console.Error.WriteLine("eof[" + s + "]=" + eof[s]);
                        for (int p = 0; p < transition[s].Length; p++) {
                            Console.Error.Write(transition[s][p] + " ");
                        }
                        Console.Error.WriteLine();
                    }
                    NoViableAlt(s, input);
                    return 0;
                }
            } finally {
                input.Rewind(mark);
            }
        }

        protected virtual void NoViableAlt(int s, IIntStream input) {
            if (recognizer.state.backtracking > 0) {
                recognizer.state.failed = true;
                return;
            }
            NoViableAltException nvae =
                new NoViableAltException(Description,
                                         decisionNumber,
                                         s,
                                         input);
            Error(nvae);
            throw nvae;
        }

        /** <summary>A hook for debugging interface</summary> */
        public virtual void Error(NoViableAltException nvae) {
        }

        public SpecialStateTransitionHandler SpecialStateTransition {
            get;
            private set;
        }
        //public virtual int specialStateTransition( int s, IntStream input )
        //{
        //    return -1;
        //}

        static int SpecialStateTransitionDefault(DFA dfa, int s, IIntStream input) {
            return -1;
        }

        public virtual string Description {
            get {
                return "n/a";
            }
        }

        /** <summary>
         *  Given a String that has a run-length-encoding of some unsigned shorts
         *  like "\1\2\3\9", convert to short[] {2,9,9,9}.  We do this to avoid
         *  static short[] which generates so much init code that the class won't
         *  compile. :(
         *  </summary>
         */
        public static short[] UnpackEncodedString(string encodedString) {
            // walk first to find how big it is.
            int size = 0;
            for (int i = 0; i < encodedString.Length; i += 2) {
                size += encodedString[i];
            }
            short[] data = new short[size];
            int di = 0;
            for (int i = 0; i < encodedString.Length; i += 2) {
                char n = encodedString[i];
                char v = encodedString[i + 1];
                // add v n times to data
                for (int j = 1; j <= n; j++) {
                    data[di++] = (short)v;
                }
            }
            return data;
        }

        /** <summary>Hideous duplication of code, but I need different typed arrays out :(</summary> */
        public static char[] UnpackEncodedStringToUnsignedChars(string encodedString) {
            // walk first to find how big it is.
            int size = 0;
            for (int i = 0; i < encodedString.Length; i += 2) {
                size += encodedString[i];
            }
            char[] data = new char[size];
            int di = 0;
            for (int i = 0; i < encodedString.Length; i += 2) {
                char n = encodedString[i];
                char v = encodedString[i + 1];
                // add v n times to data
                for (int j = 1; j <= n; j++) {
                    data[di++] = v;
                }
            }
            return data;
        }

        [Conditional("ANTLR_DEBUG")]
        protected virtual void DebugRecognitionException(RecognitionException ex) {
            IDebugEventListener dbg = recognizer.DebugListener;
            if (dbg != null)
                dbg.RecognitionException(ex);
        }
    }
}
