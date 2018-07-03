using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace ProxyCalld
{
    class Program
    {
        /// <summary>
        /// A list to store proxy method to prevent infinite loop
        /// </summary>
        public static List<MethodDef> proxyMethod = new List<MethodDef>();

        /// <summary>
        /// Save deobfuqcated file to disk
        /// </summary>
        static void savefile(ModuleDefMD mod)
        {
            string text2 = Path.GetDirectoryName(mod.Location);

            if (!text2.EndsWith("\\")) text2 += "\\";

            string path = text2 + Path.GetFileNameWithoutExtension(mod.Location) + "_deobfuscated" +
                          Path.GetExtension(mod.Location);
            var opts = new ModuleWriterOptions(mod);
            opts.Logger = DummyLogger.NoThrowInstance;
            mod.Write(path, opts);
            Console.WriteLine($"[!] File saved : {path}");
        }

        /// <summary>
        /// Entry point of protector
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            //Proxy intensity
            int intensity = 2;

            if (args.Length < 0)
            {
                Console.WriteLine("Input file missing");
                return;
            }

            ModuleDefMD mod = ModuleDefMD.Load(args[0]);
            Console.WriteLine($"[!] File loaded : {mod.Location}");

            Console.WriteLine($"     [+] starting CloningMethod protection with intensity {intensity}...");
            CloneMethods(mod, intensity);
           
            Console.WriteLine("[!] Saving file...");
            savefile(mod);

            Console.ReadKey();
        }

        /// <summary>
        /// Proxy Call protection
        /// 
        /// -Grab MethodDef
        /// -Copy MethodDef in MethodDef.Types
        /// -Replace Call to the other created Method
        /// </summary>
        static void CloneMethods(ModuleDefMD mod, int intensity = 1)
        {
            for (int o = 0; o < intensity; o++)
            {
                foreach (var t in mod.Types)
                {

                    if (t.IsGlobalModuleType) continue;

                    int mCount = t.Methods.Count;
                    for (int i = 0; i < mCount; i++)
                    {
                        var m = t.Methods[i];

                        if (!m.HasBody) continue;
                        var inst = m.Body.Instructions;

                        for (int z = 0; z < inst.Count; z++)
                        {
                            if (inst[z].OpCode == OpCodes.Call)
                            {

                                try
                                {
                                    MethodDef targetMetod = inst[z].Operand as MethodDef;

                                    /* Un comment that if you dont want to proxy methodproxy*/

                                    //if method is a proxy method
                                    //if (proxyMethod.Contains(targetMetod))
                                    //{
                                    //    //Console.WriteLine($"        [-] Method is a proxyMethod : {inst[z]}");
                                    //    continue;
                                    //}

                                    //if method is internal
                                    if (!targetMetod.FullName.Contains(mod.Assembly.Name))
                                    {
                                        //Console.WriteLine($"        [-] Method is external : {inst[z]}");
                                        continue;
                                    }

                                    //if param != 0 
                                    if (targetMetod.Parameters.Count == 0)
                                    {
                                        //Console.WriteLine($"        [-] Method has no parameters : {inst[z]}");
                                        continue;
                                    }

                                    //if param > 4 (simple Ldarg opcode) 
                                    if (targetMetod.Parameters.Count > 4)
                                    {
                                        //Console.WriteLine($"        [-] Method has too many parameters : {inst[z]}");
                                        continue;
                                    }

                                    //clone method
                                    Console.WriteLine($"        [+] Found method to clone : {inst[z]}");
                                    Console.WriteLine($"        [+] Cloning method...");
                                    MethodDef newMeth = targetMetod.copyMethod(mod);
                                    TypeDef typeOfMethod = targetMetod.DeclaringType;
                                    typeOfMethod.Methods.Add(newMeth);
                                    Console.WriteLine($"        [+] Method cloned to : {newMeth.Name}");
                                    proxyMethod.Add(newMeth);

                                    //replace method with call with param and signatures
                                    Console.WriteLine($"        [+] Editing original method...");
                                    Console.WriteLine($"            [+] Import method Attributes...");
                                    Clonesignature(targetMetod, newMeth);
                                    Console.WriteLine($"            [+] Fix call conventions...");
                                    /*
                                    nop 
                                    ldarg.0
                                    ldarg.1
                                    call
                                    ret
                                      */
                                    CilBody body = new CilBody();
                                    body.Instructions.Add(OpCodes.Nop.ToInstruction());
                                    for (int x = 0; x < targetMetod.Parameters.Count; x++)
                                    {
                                        //for future references, you will need it
                                        var typeofParam = targetMetod.Parameters[x];

                                        switch (x)
                                        {
                                            case 0:
                                                body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
                                                break;
                                            case 1:
                                                body.Instructions.Add(OpCodes.Ldarg_1.ToInstruction());
                                                break;
                                            case 2:
                                                body.Instructions.Add(OpCodes.Ldarg_2.ToInstruction());
                                                break;
                                            case 3:
                                                body.Instructions.Add(OpCodes.Ldarg_3.ToInstruction());
                                                break;
                                        }
                                    }
                                    body.Instructions.Add(Instruction.Create(OpCodes.Call, newMeth));
                                    body.Instructions.Add(OpCodes.Ret.ToInstruction());

                                    targetMetod.Body = body;
                                    Console.WriteLine($"        [+] Original method edited !");



                                }
                                catch (Exception ex)
                                {
                                    //Console.WriteLine($"        [-] Operand is not a MethodDef : {inst[z]}");
                                    //Console.WriteLine(ex.ToString());
                                    continue;
                                }

                            }
                        }

                    }

                }
            }
            
        }

        public static MethodDef Clonesignature(MethodDef from, MethodDef to)
        {
            to.Attributes = from.Attributes;

            if (from.IsHideBySig)
                to.IsHideBySig = true;

            return to;
        }

        
    }

    static class extension
    {
        /// <summary>
        ///     Context of the injection process.
        /// </summary>
        class InjectContext : ImportResolver
        {
            /// <summary>
            ///     The mapping of origin definitions to injected definitions.
            /// </summary>
            public readonly Dictionary<IDnlibDef, IDnlibDef> Map = new Dictionary<IDnlibDef, IDnlibDef>();

            /// <summary>
            ///     The module which source type originated from.
            /// </summary>
            public readonly ModuleDef OriginModule;

            /// <summary>
            ///     The module which source type is being injected to.
            /// </summary>
            public readonly ModuleDef TargetModule;

            /// <summary>
            ///     The importer.
            /// </summary>
            readonly Importer importer;

            /// <summary>
            ///     Initializes a new instance of the <see cref="InjectContext" /> class.
            /// </summary>
            /// <param name="module">The origin module.</param>
            /// <param name="target">The target module.</param>
            public InjectContext(ModuleDef module, ModuleDef target)
            {
                OriginModule = module;
                TargetModule = target;
                importer = new Importer(target, ImporterOptions.TryToUseTypeDefs);
                importer.Resolver = this;
            }

            /// <summary>
            ///     Gets the importer.
            /// </summary>
            /// <value>The importer.</value>
            public Importer Importer
            {
                get { return importer; }
            }

            /// <inheritdoc />
            public override TypeDef Resolve(TypeDef typeDef)
            {
                if (Map.ContainsKey(typeDef))
                    return (TypeDef)Map[typeDef];
                return null;
            }

            /// <inheritdoc />
            public override MethodDef Resolve(MethodDef methodDef)
            {
                if (Map.ContainsKey(methodDef))
                    return (MethodDef)Map[methodDef];
                return null;
            }

            /// <inheritdoc />
            public override FieldDef Resolve(FieldDef fieldDef)
            {
                if (Map.ContainsKey(fieldDef))
                    return (FieldDef)Map[fieldDef];
                return null;
            }
        }

        public static MethodDef copyMethod(this MethodDef originMethod, ModuleDefMD mod)
        {
            InjectContext ctx = new InjectContext(mod, mod);

            MethodDefUser newMethodDef = new MethodDefUser
            {
                Signature = ctx.Importer.Import(originMethod.Signature)
            };

            newMethodDef.Name = Guid.NewGuid().ToString().Replace("-", string.Empty);

            newMethodDef.Parameters.UpdateParameterTypes();

            if (originMethod.ImplMap != null)
                newMethodDef.ImplMap = new ImplMapUser(new ModuleRefUser(ctx.TargetModule, originMethod.ImplMap.Module.Name), originMethod.ImplMap.Name, originMethod.ImplMap.Attributes);

            foreach (CustomAttribute ca in originMethod.CustomAttributes)
                newMethodDef.CustomAttributes.Add(new CustomAttribute((ICustomAttributeType)ctx.Importer.Import(ca.Constructor)));

            if (originMethod.HasBody)
            {
                newMethodDef.Body = new CilBody(originMethod.Body.InitLocals, new List<Instruction>(), new List<ExceptionHandler>(), new List<Local>());
                newMethodDef.Body.MaxStack = originMethod.Body.MaxStack;

                var bodyMap = new Dictionary<object, object>();

                foreach (Local local in originMethod.Body.Variables)
                {
                    var newLocal = new Local(ctx.Importer.Import(local.Type));
                    newMethodDef.Body.Variables.Add(newLocal);
                    newLocal.Name = local.Name;
                    newLocal.PdbAttributes = local.PdbAttributes;

                    bodyMap[local] = newLocal;
                }

                foreach (Instruction instr in originMethod.Body.Instructions)
                {
                    var newInstr = new Instruction(instr.OpCode, instr.Operand);
                    newInstr.SequencePoint = instr.SequencePoint;

                    if (newInstr.Operand is IType)
                        newInstr.Operand = ctx.Importer.Import((IType)newInstr.Operand);

                    else if (newInstr.Operand is IMethod)
                        newInstr.Operand = ctx.Importer.Import((IMethod)newInstr.Operand);

                    else if (newInstr.Operand is IField)
                        newInstr.Operand = ctx.Importer.Import((IField)newInstr.Operand);

                    newMethodDef.Body.Instructions.Add(newInstr);
                    bodyMap[instr] = newInstr;
                }

                foreach (Instruction instr in newMethodDef.Body.Instructions)
                {
                    if (instr.Operand != null && bodyMap.ContainsKey(instr.Operand))
                        instr.Operand = bodyMap[instr.Operand];

                    else if (instr.Operand is Instruction[])
                        instr.Operand = ((Instruction[])instr.Operand).Select(target => (Instruction)bodyMap[target]).ToArray();
                }

                foreach (ExceptionHandler eh in originMethod.Body.ExceptionHandlers)
                    newMethodDef.Body.ExceptionHandlers.Add(new ExceptionHandler(eh.HandlerType)
                    {
                        CatchType = eh.CatchType == null ? null : (ITypeDefOrRef)ctx.Importer.Import(eh.CatchType),
                        TryStart = (Instruction)bodyMap[eh.TryStart],
                        TryEnd = (Instruction)bodyMap[eh.TryEnd],
                        HandlerStart = (Instruction)bodyMap[eh.HandlerStart],
                        HandlerEnd = (Instruction)bodyMap[eh.HandlerEnd],
                        FilterStart = eh.FilterStart == null ? null : (Instruction)bodyMap[eh.FilterStart]
                    });

                newMethodDef.Body.SimplifyMacros(newMethodDef.Parameters);
            }

            return newMethodDef;
        }
    }
}
