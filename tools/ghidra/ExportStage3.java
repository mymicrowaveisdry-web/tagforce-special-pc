// Export structural analysis evidence without decompiling or renaming functions.
// @category TagForce
import ghidra.app.util.headless.HeadlessScript;
import ghidra.program.model.listing.*;
import ghidra.program.model.mem.MemoryBlock;
import com.google.gson.*;
import java.nio.file.*;
import java.util.*;

public class ExportStage3 extends HeadlessScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 3) throw new IllegalArgumentException("Expected output JSON path, source SHA-256, ELF entry offset");
        JsonObject out = new JsonObject();
        out.addProperty("source_sha256", args[1]);
        out.addProperty("program", currentProgram.getName());
        out.addProperty("executable_format", currentProgram.getExecutableFormat());
        out.addProperty("language", currentProgram.getLanguageID().toString());
        out.addProperty("compiler_spec", currentProgram.getCompilerSpec().getCompilerSpecID().toString());
        out.addProperty("image_base", currentProgram.getImageBase().toString());
        out.addProperty("instruction_count", currentProgram.getListing().getNumInstructions());
        out.addProperty("function_count", currentProgram.getFunctionManager().getFunctionCount());
        out.addProperty("analysis_timed_out", isRunningHeadless() && analysisTimeoutOccurred());
        var entryAddress = currentProgram.getImageBase().add(Long.decode(args[2]));
        byte[] entryCode = new byte[128];
        currentProgram.getMemory().getBytes(entryAddress, entryCode);
        out.addProperty("elf_entry_rebased", entryAddress.toString());
        out.addProperty("entry_code_base64", Base64.getEncoder().encodeToString(entryCode));
        out.addProperty("entry_has_instruction", currentProgram.getListing().getInstructionAt(entryAddress) != null);
        out.addProperty("entry_has_function", currentProgram.getFunctionManager().getFunctionAt(entryAddress) != null);
        JsonArray blocks = new JsonArray();
        for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
            JsonObject b = new JsonObject();
            b.addProperty("name", block.getName());
            b.addProperty("start", block.getStart().toString());
            b.addProperty("end", block.getEnd().toString());
            b.addProperty("size", block.getSize());
            b.addProperty("read", block.isRead());
            b.addProperty("write", block.isWrite());
            b.addProperty("execute", block.isExecute());
            b.addProperty("initialized", block.isInitialized());
            blocks.add(b);
        }
        out.add("memory_blocks", blocks);
        JsonArray functions = new JsonArray();
        FunctionIterator it = currentProgram.getFunctionManager().getFunctions(true);
        while (it.hasNext()) {
            monitor.checkCancelled();
            Function f = it.next();
            JsonObject entry = new JsonObject();
            entry.addProperty("address", f.getEntryPoint().toString());
            entry.addProperty("name", f.getName());
            entry.addProperty("symbol_source", f.getSymbol().getSource().toString());
            entry.addProperty("body_bytes", f.getBody().getNumAddresses());
            functions.add(entry);
        }
        out.add("functions", functions);
        Path path = Paths.get(args[0]);
        Files.createDirectories(path.getParent());
        Files.writeString(path, new GsonBuilder().setPrettyPrinting().create().toJson(out) + "\n");
        println("STAGE3_EXPORT " + path);
    }
}
