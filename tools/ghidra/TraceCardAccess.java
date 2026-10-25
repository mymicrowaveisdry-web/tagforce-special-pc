// Trace exact table-name literal references without changing the program.
// @category TagForce
import ghidra.app.util.headless.HeadlessScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.address.*;
import ghidra.program.model.symbol.*;
import com.google.gson.*;
import java.nio.file.*;
import java.util.*;

public class TraceCardAccess extends HeadlessScript {
    public void run() throws Exception {
        String[] args = getScriptArgs();
        JsonObject out = new JsonObject();
        JsonArray anchors = new JsonArray();
        TreeMap<String, Function> functions = new TreeMap<>();
        for (int i = 1; i < args.length; i += 2) {
            Address address = toAddr(args[i+1]);
            JsonObject a = new JsonObject();
            a.addProperty("literal", args[i]); a.addProperty("address", address.toString());
            if (args[i].startsWith("Function:")) {
                Function target = getFunctionAt(address);
                if (target != null) functions.put(target.getEntryPoint().toString(), target);
            }
            JsonArray references = new JsonArray();
            for (Reference ref : currentProgram.getReferenceManager().getReferencesTo(address)) {
                JsonObject r = new JsonObject();
                r.addProperty("from", ref.getFromAddress().toString());
                r.addProperty("type", ref.getReferenceType().toString());
                Function f = getFunctionContaining(ref.getFromAddress());
                if (f != null) {
                    r.addProperty("function", f.getEntryPoint().toString());
                    functions.put(f.getEntryPoint().toString(), f);
                }
                references.add(r);
            }
            a.add("references", references); anchors.add(a);
        }
        out.add("anchors", anchors);
        JsonArray bodies = new JsonArray();
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function f : functions.values()) {
                JsonObject b = new JsonObject();
                b.addProperty("entry", f.getEntryPoint().toString()); b.addProperty("name", f.getName());
                DecompileResults result = decompiler.decompileFunction(f, 60, monitor);
                b.addProperty("complete", result.decompileCompleted());
                if (result.decompileCompleted()) b.addProperty("c", result.getDecompiledFunction().getC());
                JsonArray called = new JsonArray();
                for (Function callee : f.getCalledFunctions(monitor)) called.add(callee.getEntryPoint().toString());
                b.add("callees", called); bodies.add(b);
            }
        } finally { decompiler.dispose(); }
        out.add("functions", bodies);
        Files.writeString(Paths.get(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(out));
    }
}
