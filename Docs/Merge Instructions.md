# Merge a Working Branch into `master`

Run these commands from the repository root. Replace `<working-branch>` with the
name of the branch you want to merge. The examples assume the remote is named
`origin`.

## 1. Confirm your current branch and changes

```powershell
git branch --show-current
git status
```

## 2. Commit any unfinished work on the working branch

Skip this step if `git status` reports that the working tree is clean.

```powershell
git add --all
git commit -m "<commit message>"
```

## 3. Push the working branch

```powershell
git push -u origin <working-branch>
```

After the upstream is configured, later pushes only require `git push`.

## 4. Update local `master`

```powershell
git switch master
git pull --ff-only origin master
```

## 5. Merge the working branch into `master`

```powershell
git merge <working-branch>
```

If Git reports conflicts, edit the conflicted files and then finish the merge:

```powershell
git status
git add <resolved-file-1> <resolved-file-2>
git commit
```

## 6. Verify the merged result

Run the repository's relevant build and tests. For the .NET solution:

```powershell
dotnet build VocabularyApp.sln
```

Then inspect the final state:

```powershell
git status
git log --oneline --decorate -10
```

## 7. Push `master`

```powershell
git push origin master
```

## Optional: delete the merged branch

Only do this after the merge is verified and `master` has been pushed.

```powershell
git branch -d <working-branch>
git push origin --delete <working-branch>
```

If `master` is protected, push the working branch and create a pull request instead
of performing steps 4, 5, and 7 directly. Merge the pull request after its required
checks and approvals pass.
