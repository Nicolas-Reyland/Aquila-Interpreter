﻿using System.Collections.Generic;

// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable PossibleNullReferenceException
// ReSharper disable ArrangeObjectCreationWhenTypeEvident

namespace Parser
{
    public abstract class Instruction
    {
        protected int line_index;

        // methods
        public abstract void execute();
        protected void setLineIndex() => Global.current_line_index = line_index;
        protected abstract void setContext();
    }

    internal abstract class NestedInstruction : Instruction // ex: for, while, if, etc.
    {
        // attributes
        protected int depth;
        internal List<Instruction> instructions;
    }

    internal abstract class Loop : NestedInstruction //! NEVER NEED TO UPDATE TRACERS IN LOOPS (already in all sub-instructions)
    {
        private readonly Expression _condition;
        protected bool in_loop;

        protected Loop(int line_index, Expression condition, List<Instruction> instructions)
        {
            this.line_index = line_index;
            this._condition = condition;
            this.instructions = instructions;
            this.line_index = line_index;
        }

        protected bool test()
        {
           Variable cond = _condition.evaluate();
           Debugging.assert(cond is BooleanVar); // TypeError
           bool bool_cond = cond.getValue();
           return bool_cond;
        }

        public bool isInLoop() => in_loop;
    }

    internal class WhileLoop : Loop
    {
        public WhileLoop(int line_index, Expression condition, List<Instruction> instructions) : base(line_index, condition, instructions)
        {
            //
        }

        protected override void setContext()
        {
            setLineIndex();
            Context.setStatus(Context.StatusEnum.while_loop_execution);
            Context.setInfo(this);
            Global.newLocalContextScope();
        }

        public override void execute()
        {
            setContext();
            int context_integrity_check = Context.getStatusStackCount();
            while (test())
            {
                in_loop  = true;
                foreach (Instruction instr in instructions)
                {
                    Global.newLocalContextScope();
                    instr.execute();
                    Global.resetLocalContextScope();
                }
            }
            in_loop = false;
            Debugging.assert(context_integrity_check == Context.getStatusStackCount()); // should be the same
            Context.reset();
            Global.resetLocalContextScope();
        }
    }

    internal class ForLoop : Loop
    {
        private readonly Instruction _start;
        private readonly Instruction _step;

        public ForLoop(int line_index, Instruction start, Expression condition, Instruction step,
            List<Instruction> instructions) : base(line_index, condition, instructions)
        {
            _start = start;
            _step = step;
        }

        protected override void setContext()
        {
            setLineIndex();
            Context.setStatus(Context.StatusEnum.for_loop_execution);
            Context.setInfo(this);
            Global.newLocalContextScope();
        }

        public override void execute()
        {
            setContext();
            int context_integrity_check = Context.getStatusStackCount();
            _start.execute();
            while (test())
            {
                in_loop  = true;
                foreach (Instruction instr in instructions)
                {
                    Global.newLocalContextScope();
                    instr.execute();
                    Global.resetLocalContextScope();
                }
                // executing step independently bc of continue & break
                _step.execute();
            }
            in_loop = false;
            Debugging.assert(context_integrity_check == Context.getStatusStackCount()); // should be the same
            Context.reset();
            Global.resetLocalContextScope();
          }
    }

    internal class IfCondition : NestedInstruction // Don't need to update tracers here either
    {
        private readonly Expression _condition;
        private readonly List<Instruction> _else_instructions;

        public IfCondition(int line_index, Expression condition, List<Instruction> instructions, List<Instruction> else_instructions)
        {
            this.line_index = line_index;
            this._condition = condition;
            this.instructions = instructions;
            this._else_instructions = else_instructions;
        }

        protected override void setContext()
        {
            setLineIndex();
            Context.setStatus(Context.StatusEnum.if_execution);
            Context.setInfo(this);
            Global.newLocalContextScope();
        }

        public override void execute()
        {
            setContext();
            int context_integrity_check = Context.getStatusStackCount();
            
            if ( ((BooleanVar) _condition.evaluate()).getValue() )
            {
                foreach (Instruction instr in instructions)
                {
                    instr.execute();
                }
            }
            else
            {
                foreach (Instruction instr in _else_instructions)
                {
                    instr.execute();
                }
            }

            Debugging.assert(context_integrity_check == Context.getStatusStackCount()); // should be the same
            Context.reset();
            Global.resetLocalContextScope();
        }
    }

    internal class Declaration : Instruction
    {
        private readonly string _var_name;
        private readonly Expression _var_expr;
        private readonly string _var_type;
        private readonly bool _assignment;

        public Declaration(int line_index, string var_name, Expression var_expr, string var_type = "auto", bool assignment = true, int overwrite = 0)
        {
            Debugging.assert(StringUtils.validObjectName(var_name)); // InvalidNamingException
            // overwrite: 0 -> new var (must not exist); 1 -> force overwrite (must exist); 2 -> safe overwrite (can exist)
            this.line_index = line_index;
            this._var_name = var_name;
            this._var_expr = var_expr;
            this._var_type = var_type;
            this._assignment = assignment;
            // check variable naming
            Debugging.assert(StringUtils.validObjectName(var_name)); // InvalidNamingException
            // the declaration is not initiated in the right scope... cannot do this here
            bool var_exists = Global.variableExistsInCurrentScope(var_name);
            Debugging.print("new declaration: var_name = " + var_name + ", var_expr = " + var_expr.expr + ", var_type = " + var_type + ", assignment = ", assignment, ", overwrite = ", overwrite, ", exists = ", var_exists);
            // check variable existence
            if (overwrite == 0) Debugging.assert(!var_exists); // DeclaredExistingVarException
            if (overwrite == 1) Debugging.assert(var_exists); // OverwriteNonExistingVariable
            // check for overwrite + tracer
            if (overwrite != 0 && var_exists) Debugging.assert(!Global.variableFromName(var_name).isTraced());
        }

        protected override void setContext()
        {
            setLineIndex();
            Context.setStatus(Context.StatusEnum.declaration_execution);
            Context.setInfo(this);
        }

        public override void execute()
        {
            setContext();
            int context_integrity_check = Context.getStatusStackCount();

            // get variable value
            Variable variable = _var_expr.evaluate();
            // is the value assigned ? (only relevant if other variable)
            variable.assertAssignment();
            // explicit typing
            if (_var_type != "auto")
            {
                Debugging.print("checking variable explicit type");
                Expression default_value = Global.default_values_by_var_type[_var_type];
                Debugging.assert( variable.hasSameParent(default_value.evaluate()) ); // TypeException
            }
            // actually declare it to its value
            Global.getCurrentDict()[_var_name] = Variable.fromRawValue(variable.getRawValue()); // overwriting is mandatory
            Global.getCurrentDict()[_var_name].setName(_var_name);
            if (_assignment) variable.assign(); // should not need this, but doing anyway
            else variable.assigned = false;
            variable.setName(_var_name);
            Debugging.print("finished declaration with value assignment: ", variable.assigned);

            // update all tracers
            Tracer.updateTracers();

            // reset Context
            Debugging.assert(context_integrity_check == Context.getStatusStackCount()); // should be the same
            Context.reset();
        }
    }

    internal class Assignment : Instruction
    {
        private readonly string _var_name;
        private readonly Expression _var_value;

        public Assignment(int line_index, string var_name, Expression var_value)
        {
            this.line_index = line_index;
            _var_name = var_name;
            _var_value = var_value;
            // cannot check if variable is in dict or not (e.g. $l[0])
            Debugging.assert(_var_name != ""); // check this now to prevent later errors
        }

        protected override void setContext()
        {
            setLineIndex();
            Context.setStatus(Context.StatusEnum.assignment_execution);
            Context.setInfo(this);
        }

        public override void execute()
        {
            setContext();
            int context_integrity_check = Context.getStatusStackCount();

            // parsing new value
            Variable val = _var_value.evaluate();
            Debugging.print("assigning " + _var_name + " with expr " + _var_value.expr + " with value " + val + " (2nd value assigned: " + val.assigned + ") and type: " + val.getTypeString());
            // assert the new is not an unassigned (only declared) variable
            val.assertAssignment();
            // set the new value
            Expression.parse(_var_name).setValue(val);

            // update all tracers
            Tracer.updateTracers();

            // reset Context
            Debugging.assert(context_integrity_check == Context.getStatusStackCount()); // should be the same
            Context.reset();
        }
    }

    internal class VoidFunctionCall : Instruction
    {
        private readonly string _function_name;
        private readonly object[] _args;
        private bool _called;

        public VoidFunctionCall(int line_index, string function_name, params object[] args)
        {
            this.line_index = line_index;
            _function_name = function_name;
            _args = args;
        }

        protected override void setContext()
        {
            setLineIndex();
            Context.setStatus(Context.StatusEnum.predefined_function_call);
            Context.setInfo(this);
        }

        public override void execute()
        {
            setContext();
            int context_integrity_check = Context.getStatusStackCount();

            _called = true;
            Functions.callFunctionByName(_function_name, _args);
            // update all tracers
            Tracer.updateTracers();

            Debugging.assert(context_integrity_check == Context.getStatusStackCount()); // should be the same
            Context.reset();
        }

        public object[] getArgs() => _args;

        public bool hasBeenCalled() => _called;
    }

    internal class FunctionDef : Instruction
    {
        private readonly Function _func;
        public FunctionDef(int line_index, Function func)
        {
            this.line_index = line_index;
            this._func = func;
        }

        protected override void setContext()
        {
            setLineIndex();
            Context.setStatus(Context.StatusEnum.user_function_call);
            Context.setInfo(this);
        }
        
        public override void execute()
        {
            setContext();
            int context_integrity_check = Context.getStatusStackCount();
            Functions.addUserFunction(_func);
            Debugging.assert(context_integrity_check == Context.getStatusStackCount()); // should be the same
            Context.reset();
        }
    }

    internal class Tracing : Instruction // no updateTracers bc values can't be changed here ...
    {
        private readonly List<Expression> _traced_vars;

        public Tracing(int line_index, List<Expression> traced_vars)
        {
            this.line_index = line_index;
            this._traced_vars = traced_vars;
        }

        protected override void setContext()
        {
            setLineIndex();
            Context.setStatus(Context.StatusEnum.trace_execution);
            Context.setInfo(this);
        }

        public override void execute()
        {
            setContext();
            int context_integrity_check = Context.getStatusStackCount();

            // the tracing instruction execution doesn't take any Tracer.updateTracers() calls
            foreach (Expression traced_expr in _traced_vars)
            {
                Variable traced_var = traced_expr.evaluate();
                Debugging.assert(!traced_var.isTraced());
                traced_var.startTracing();
            }

            Debugging.assert(context_integrity_check == Context.getStatusStackCount()); // should be the same
            Context.reset();
        }
    }
}
