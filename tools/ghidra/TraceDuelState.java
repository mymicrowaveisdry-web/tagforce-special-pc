// Export candidate state references and a bounded sample of small accessors; no renames.
// @category TagForce
import ghidra.app.util.headless.HeadlessScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import com.google.gson.*;
import java.nio.file.*;
import java.util.*;

public class TraceDuelState extends HeadlessScript {
    public void run() throws Exception {
        String[] args=getScriptArgs();
        JsonObject out=new JsonObject();
        JsonArray anchors=new JsonArray();
        TreeMap<String,Function> selected=new TreeMap<>();
        for(int i=1;i<args.length;i+=2) {
            var address=toAddr(args[i+1]);
            JsonObject anchor=new JsonObject();
            anchor.addProperty("candidate",args[i]);
            anchor.addProperty("address",address.toString());
            JsonArray refs=new JsonArray();
            if(args[i].startsWith("Function:")) {
                Function f=getFunctionAt(address);
                if(f!=null) selected.put(f.getEntryPoint().toString(),f);
                anchor.add("references",refs);
                anchors.add(anchor);
                continue;
            }
            int count=0;
            for(Reference ref:currentProgram.getReferenceManager().getReferencesTo(address)) {
                JsonObject item=new JsonObject();
                item.addProperty("from",ref.getFromAddress().toString());
                item.addProperty("type",ref.getReferenceType().toString());
                Function f=getFunctionContaining(ref.getFromAddress());
                if(f!=null) {
                    item.addProperty("function",f.getEntryPoint().toString());
                    item.addProperty("body_bytes",f.getBody().getNumAddresses());
                    if(f.getBody().getNumAddresses()<=256 && count<6) {
                        selected.put(f.getEntryPoint().toString(),f);
                        count++;
                    }
                }
                refs.add(item);
            }
            anchor.add("references",refs);
            anchors.add(anchor);
        }
        out.add("anchors",anchors);
        JsonArray functions=new JsonArray();
        DecompInterface decompiler=new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for(Function f:selected.values()) {
                monitor.checkCancelled();
                JsonObject item=new JsonObject();
                item.addProperty("entry",f.getEntryPoint().toString());
                DecompileResults result=decompiler.decompileFunction(f,15,monitor);
                item.addProperty("complete",result.decompileCompleted());
                if(result.decompileCompleted()) item.addProperty("c",result.getDecompiledFunction().getC());
                functions.add(item);
            }
        } finally {decompiler.dispose();}
        out.add("functions",functions);
        Files.writeString(Paths.get(args[0]),new GsonBuilder().setPrettyPrinting().create().toJson(out)+"\n");
    }
}
